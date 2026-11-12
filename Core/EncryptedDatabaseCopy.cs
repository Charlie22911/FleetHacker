using System;
using System.IO;
using System.Linq;
using System.Threading;
namespace MediaReviewer {
    internal static class EncryptedDatabaseCopy {
        static string Quote(string name){return "\""+name.Replace("\"","\"\"")+"\"";}
        internal static void Create(SqliteConnection source,string output,DatabaseCredentials access,CancellationToken token,Action<long,long,string> progress){
            string destination=Paths.LocalDatabase(output);
            if(File.Exists(Paths.Io(destination)))throw new IOException("Destination exists; choose a new database name.");
            DatabaseProtection.RequireDifferentSupervisor(source,access.Database,token);
            string partial=destination+".partial-"+Guid.NewGuid().ToString("N");bool transaction=false;
            try{
                // Copy logical rows into an already keyed destination. A plaintext staging file is never created.
                source.Execute("BEGIN");transaction=true;
                var schema=source.Query("SELECT type,name,sql FROM sqlite_schema WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%' ORDER BY type,name",null,10000);
                using(var target=new SqliteConnection(partial,false,access.Database)){
                    target.Script("PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA foreign_keys=OFF; PRAGMA cache_size=-4096; BEGIN IMMEDIATE;");
                    foreach(var item in schema.Where(r=>(string)r[0]=="table")){token.ThrowIfCancellationRequested();target.Script((string)item[2]);}
                    long copied=0;
                    foreach(var item in schema.Where(r=>(string)r[0]=="table")){
                        string table=Quote((string)item[1]);
                        var columns=source.Query("SELECT name FROM pragma_table_xinfo(?) WHERE hidden=0 ORDER BY cid",new object[]{item[1]},10000).Select(row=>Quote((string)row[0])).ToList();
                        if(Convert.ToInt64(source.Scalar("SELECT wr FROM pragma_table_list WHERE schema='main' AND name=?",item[1]))==0)columns.Insert(0,"_rowid_");
                        string fields=String.Join(",",columns);
                        using(var input=source.Prepare("SELECT "+fields+" FROM "+table)){
                            SqliteStatement insert=null;
                            try{while(input.Step()){
                                token.ThrowIfCancellationRequested();var row=input.Row();
                                if(insert==null)insert=target.Prepare("INSERT INTO "+table+" ("+fields+") VALUES("+String.Join(",",row.Select(v=>"?"))+")");
                                insert.Bind(row);while(insert.Step()){}if(++copied%128==0)progress(copied,-1,"records encrypted");
                            }}finally{if(insert!=null)insert.Dispose();}
                        }
                    }
                    if(source.Scalar("SELECT 1 FROM sqlite_schema WHERE name='sqlite_sequence'")!=null){
                        target.Execute("DELETE FROM sqlite_sequence");using(var input=source.Prepare("SELECT name,seq FROM sqlite_sequence"))while(input.Step()){token.ThrowIfCancellationRequested();target.Execute("INSERT INTO sqlite_sequence VALUES(?,?)",input.Value(0),input.Value(1));}
                    }
                    foreach(var item in schema.Where(r=>(string)r[0]!="table")){token.ThrowIfCancellationRequested();target.Script((string)item[2]);}
                    target.Script("PRAGMA application_id="+Convert.ToInt64(source.Scalar("PRAGMA application_id"))+"; PRAGMA user_version="+Convert.ToInt64(source.Scalar("PRAGMA user_version"))+";");
                    if(target.Scalar("SELECT 1 FROM evidence_vault WHERE id=1")==null){if(access.Supervisor==null)throw new InvalidOperationException("Choose a Supervisor password for this database.");DatabaseProtection.CreateSupervisor(target,access.Supervisor);}
                    token.ThrowIfCancellationRequested();target.Execute("COMMIT");
                    if((string)target.Scalar("PRAGMA quick_check")!="ok")throw new InvalidDataException("Encrypted copy did not pass its integrity check.");
                    if(target.Scalar("PRAGMA foreign_key_check")!=null)throw new InvalidDataException("Encrypted copy contains broken record references.");
                    progress(copied,copied,"records encrypted");
                }
                source.Execute("COMMIT");transaction=false;token.ThrowIfCancellationRequested();
                if(!DatabaseProtection.IsEncrypted(partial))throw new InvalidDataException("Encrypted copy verification failed.");
                DatabaseProtection.Validate(partial,access.Database);File.Move(Paths.Io(partial),Paths.Io(destination));
            }finally{
                if(transaction)source.ConfirmRollback();
                foreach(string suffix in new[]{"","-journal","-wal","-shm"})try{File.Delete(Paths.Io(partial+suffix));}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
    }
}
