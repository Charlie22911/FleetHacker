using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
namespace MediaReviewer {
    // Session credentials are never placed in SQL, process arguments or database settings.
    internal sealed class SessionPassword:IDisposable {
        readonly object gate=new object();byte[] protectedBytes;readonly int length;
        internal SessionPassword(byte[] bytes){
            try{if(bytes==null || bytes.Length<1 || bytes.Length>65536)throw new ArgumentException("Enter a non-empty password.");
                length=bytes.Length;protectedBytes=new byte[(length+15)/16*16];Buffer.BlockCopy(bytes,0,protectedBytes,0,length);
                ProtectedMemory.Protect(protectedBytes,MemoryProtectionScope.SameProcess);
            }catch{Dispose();throw;}finally{if(bytes!=null)Array.Clear(bytes,0,bytes.Length);}
        }
        internal byte[] Read(){lock(gate){if(protectedBytes==null)throw new ObjectDisposedException("Database session password");byte[] padded=(byte[])protectedBytes.Clone();try{ProtectedMemory.Unprotect(padded,MemoryProtectionScope.SameProcess);var result=new byte[length];Buffer.BlockCopy(padded,0,result,0,length);return result;}finally{Array.Clear(padded,0,padded.Length);}}}
        public void Dispose(){lock(gate){if(protectedBytes!=null){Array.Clear(protectedBytes,0,protectedBytes.Length);protectedBytes=null;}}}
    }
    internal sealed class DatabaseCredentials:IDisposable {
        internal SessionPassword Database,Supervisor;
        internal DatabaseCredentials(byte[] database,byte[] supervisor=null){
            try{if(supervisor!=null && database!=null && database.SequenceEqual(supervisor))throw new ArgumentException("Choose different database and Supervisor passwords.");
                Database=new SessionPassword(database);if(supervisor!=null)Supervisor=new SessionPassword(supervisor);
            }catch{Dispose();throw;}finally{if(database!=null)Array.Clear(database,0,database.Length);if(supervisor!=null)Array.Clear(supervisor,0,supervisor.Length);}
        }
        public void Dispose(){if(Database!=null)Database.Dispose();if(Supervisor!=null)Supervisor.Dispose();}
    }
    internal static class DatabaseProtection {
        const int Iterations=600000;
        static readonly object gate=new object();
        static readonly Dictionary<string,SessionPassword> sessions=new Dictionary<string,SessionPassword>(StringComparer.OrdinalIgnoreCase);
        internal static bool IsEncrypted(string path){
            if(!File.Exists(Paths.Io(path)))return false;
            using(var file=new FileStream(Paths.Io(path),FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){
                if(file.Length<16)throw new InvalidDataException("The selected database is empty or incomplete.");
                var header=new byte[16];if(file.Read(header,0,16)!=16)throw new EndOfStreamException();
                return Encoding.ASCII.GetString(header)!="SQLite format 3\0";
            }
        }
        internal static void Register(string path,SessionPassword password){lock(gate){path=Path.GetFullPath(path);if(sessions.ContainsKey(path))throw new InvalidOperationException("This database is already open in this process.");sessions.Add(path,password);}}
        internal static void Unregister(string path,SessionPassword password){lock(gate){SessionPassword found;path=Path.GetFullPath(path);if(sessions.TryGetValue(path,out found) && ReferenceEquals(found,password))sessions.Remove(path);}}
        internal static SessionPassword Find(string path){lock(gate){SessionPassword value;return sessions.TryGetValue(Path.GetFullPath(path),out value) ? value : null;}}
        internal static void Key(SqliteConnection db,SessionPassword secret){
            if(secret==null)return;
            if(Sql.sqlite3mc_config_cipher(db.Handle,Native.Text("chacha20"),Native.Text("kdf_iter"),Iterations)!=Iterations)throw new CryptographicException("Database key derivation could not be configured.");
            byte[] bytes=secret.Read();try{db.Check(Sql.sqlite3_key(db.Handle,bytes,bytes.Length));}finally{Array.Clear(bytes,0,bytes.Length);}
        }
        internal static void Validate(string path,SessionPassword password){
            try{using(var db=new SqliteConnection(path,true,password)){
                if(Convert.ToInt64(db.Scalar("PRAGMA application_id"))!=Catalog.ApplicationId)throw new InvalidDataException("This file is not a Media Review database.");
            }}catch(DatabaseException error){throw new IOException("The database could not be unlocked. Check the password and file integrity.",error);}
        }
        internal static void CreateSupervisor(SqliteConnection db,SessionPassword password){
            byte[] salt=EvidenceFormat.Random(16),nonce=EvidenceFormat.Random(12);
            using(var key=Crypto.DeriveKey(password.Read(),salt,Iterations)){
                byte[] check=Encoding.UTF8.GetBytes("ImageComplianceReviewer evidence vault v1");
                byte[] cipher=Crypto.Encrypt(key,nonce,EvidenceFormat.CheckAad(EvidenceFormat.Workspace(db)),check);
                db.Execute("INSERT INTO evidence_vault VALUES(1,?,?,?,?,?)",salt,Iterations,nonce,cipher,Catalog.Now());
            }
        }
        internal static bool HasImportedSupervisor(string path){using(var db=new SqliteConnection(path,true))return db.Scalar("SELECT 1 FROM sqlite_schema WHERE type='table' AND name='evidence_vault'")!=null && db.Scalar("SELECT 1 FROM evidence_vault LIMIT 1")!=null;}
        internal static void RequireDifferentSupervisor(SqliteConnection db,SessionPassword password,CancellationToken token){
            if(db.Scalar("SELECT 1 FROM evidence_vault WHERE id=1")==null)return;
            bool same=false;try{using(var key=VaultDescriptor.Read(db).Open(EvidenceFormat.Workspace(db),password.Read(),token))same=true;}catch(CryptographicException){}
            if(same)throw new ArgumentException("Choose a database password different from the current Supervisor password.");
        }
    }
    public sealed partial class ReviewerEngine {
        DatabaseCredentials credentials;
        public bool DatabaseEncrypted {get{return credentials!=null;}}
        internal static ReviewerEngine OpenProtected(string path,bool create,DatabaseCredentials access){
            if(access==null || create && access.Supervisor==null)throw new ArgumentException("Database setup requires database and Supervisor passwords.");
            try{return new ReviewerEngine(path,create,null,null,access);}catch{access.Dispose();throw;}
        }
        internal static ReviewerEngine ImportHtmlProtected(string input,string output,DatabaseCredentials access){
            if(access==null)throw new ArgumentNullException("access");
            try{return new ReviewerEngine(output,true,input,null,access);}catch{access.Dispose();throw;}
        }
        internal string RequestEncryptedCopy(string output,DatabaseCredentials access){
            if(access==null)throw new ArgumentNullException("access");
            return EnqueueOperation("Encrypted database copy",(db,t,p)=>{
                try{
                    EncryptedDatabaseCopy.Create(db,output,access,t,p);return "Encrypted copy saved: "+output+". The original keeps its existing protection.";
                }
                finally{access.Dispose();}
            },4096,access.Dispose);
        }
        bool MatchesDatabasePassword(byte[] proposed){
            if(credentials==null)return false;byte[] current=credentials.Database.Read();try{return current.SequenceEqual(proposed);}finally{Array.Clear(current,0,current.Length);}
        }
    }
}
