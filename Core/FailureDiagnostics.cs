using System;
using System.Collections.Generic;

namespace MediaReviewer {
    internal static class FailureDiagnostics {
        static readonly object gate=new object();
        static readonly Queue<string> entries=new Queue<string>();
        internal static string Brief(Exception error){string text=error.GetBaseException().Message??error.GetType().Name;return text.Length>2048 ? text.Substring(0,2048)+"…" : text;}
        internal static void Record(string context,Exception error){
            string text=context+": "+error; if(text.Length>8192)text=text.Substring(0,8192);
            lock(gate){while(entries.Count>=16)entries.Dequeue();entries.Enqueue(text);}
            System.Diagnostics.Trace.WriteLine(text);
        }
        internal static string[] Snapshot(){lock(gate)return entries.ToArray();}
    }
    // Only rendering code that owns and discards the failed visual may use this marker.
    internal sealed class PresentationFailureException:Exception {
        internal PresentationFailureException(Exception error):base("A preview could not be displayed. Other review controls remain available.",error){}
    }
    internal sealed class UncertainDatabaseStateException:System.IO.IOException {
        internal UncertainDatabaseStateException(Exception error):base("Database rollback could not be confirmed. Reopen the database before retrying.",error){}
    }
}
