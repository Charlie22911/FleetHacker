using System;
using System.Globalization;
using System.IO;
namespace MediaReviewer {
    public sealed class OriginalStorageLimits {
        public long DatabaseBytes,FileBytes;
        internal const string DatabaseKey="original_limits.v2.database_bytes",FileKey="original_limits.v2.file_bytes";
        static long Bytes(double gb){
            if(Double.IsNaN(gb) || Double.IsInfinity(gb) || gb<0)throw new ArgumentException("Enter a non-negative limit in GB; zero means no fixed cap.");
            try{decimal value=(decimal)gb*1000000000m;if(value>Int64.MaxValue)throw new OverflowException();return checked((long)Decimal.Ceiling(value));}
            catch(OverflowException){throw new ArgumentException("The selected limit is too large to represent.");}
        }
        public static OriginalStorageLimits FromGigabytes(double database,double original){return new OriginalStorageLimits {DatabaseBytes=Bytes(database),FileBytes=Bytes(original)};}
        internal static OriginalStorageLimits Read(SqliteConnection db){return new OriginalStorageLimits {DatabaseBytes=Setting(db,DatabaseKey),FileBytes=Setting(db,FileKey)};}
        static long Setting(SqliteConnection db,string key){object raw=db.Scalar("SELECT value FROM settings WHERE key=?",key);if(raw==null)return 0;long value;if(!Int64.TryParse((string)raw,NumberStyles.None,CultureInfo.InvariantCulture,out value) || value<0)throw new InvalidDataException("A retained-original limit is invalid. Open Maintenance > Retention limits to save new limits.");return value;}
        public int CheckOriginal(long size){
            if(size<0)throw new InvalidDataException("Original size is invalid.");
            if(FileBytes>0 && size>FileBytes)throw new IOException("The selected original file limit would be exceeded. Change Maintenance > Retention limits; the review was not published.");
            long count=size/EvidenceFormat.ChunkSize+(size%EvidenceFormat.ChunkSize==0 ? 0 : 1);
            if(count>Int32.MaxValue)throw new IOException("Original exceeds the authenticated chunk format's capacity.");
            return (int)count;
        }
        public void CheckProjection(long allocated,long wal,long additional,long free){
            if(allocated<0 || wal<0 || additional<0)throw new InvalidDataException("Invalid retained-original storage projection.");
            if(DatabaseBytes>0 && (allocated>DatabaseBytes || wal>DatabaseBytes-allocated || additional>DatabaseBytes-allocated-wal))throw new IOException("The selected database limit would be exceeded by the database, WAL and pending original. Change Maintenance > Retention limits; the review was not published.");
            const long reserve=64L*1024*1024;
            if(additional>(Int64.MaxValue-reserve)/2 || free<additional*2+reserve)throw new IOException("Insufficient free space for retained originals and SQLite working space; no review was published.");
        }
    }
    public sealed partial class ReviewerEngine {
        public string RequestOriginalLimits(){return EnqueueOperation("OriginalLimitsRead",(db,t,p)=>EvidenceFormat.Json().Serialize(OriginalStorageLimits.Read(db)));}
        public string RequestOriginalLimits(double databaseGigabytes,double originalGigabytes){
            var limits=OriginalStorageLimits.FromGigabytes(databaseGigabytes,originalGigabytes);
            return EnqueueOperation("OriginalLimitsSaved",(db,t,p)=>{WaitWrite(w=>{Verification.Transaction(w,()=>{
                w.Execute("INSERT INTO settings VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",OriginalStorageLimits.DatabaseKey,limits.DatabaseBytes.ToString(CultureInfo.InvariantCulture));
                w.Execute("INSERT INTO settings VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",OriginalStorageLimits.FileKey,limits.FileBytes.ToString(CultureInfo.InvariantCulture));
            });return null;});return "Retention limits committed. Database: "+Label(limits.DatabaseBytes)+"; original file: "+Label(limits.FileBytes)+". Free working space is always checked.";});
        }
        static string Label(long bytes){return bytes==0 ? "no fixed cap" : (bytes/1000000000m).ToString("0.#########",CultureInfo.InvariantCulture)+" GB";}
    }
}
