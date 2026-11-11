using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediaReviewer {
    // The catalog serializes access: a draft borrows the loaded worker until it finishes.
    internal sealed class GemmaSession {
        readonly bool cpuOnly;
        readonly string criteria;
        readonly int side;
        readonly CancellationTokenSource stop=new CancellationTokenSource();
        readonly Task<DraftModelWorker> load;

        internal GemmaSession(bool cpuOnly,string criteria,Task previous,int side=256){
            this.cpuOnly=cpuOnly;this.criteria=criteria;this.side=side;
            load=Task.Run(async ()=>{
                if(previous!=null)await previous.ConfigureAwait(false);
                stop.Token.ThrowIfCancellationRequested();
                var worker=new DraftModelWorker(true,cpuOnly,criteria,side);
                try{worker.Prepare(stop.Token);return worker;}
                catch{worker.Dispose();throw;}
            });
        }

        internal bool Matches(bool cpu,string rubric,int resolution=256){return cpuOnly==cpu && criteria==rubric && side==resolution;}
        internal bool Failed {get{return load.IsFaulted || load.IsCanceled;}}
        internal bool Ready {get{return load.Status==TaskStatus.RanToCompletion;}}
        internal string Status {
            get{
                if(Ready)return "Gemma is ready · "+load.Result.Device;
                if(load.IsFaulted)return "Gemma could not load. "+load.Exception.GetBaseException().Message;
                return "Loading Gemma in the background…";
            }
        }

        internal DraftModelWorker Wait(CancellationToken token){
            try{load.Wait(token);}
            catch(AggregateException){return load.GetAwaiter().GetResult();}
            return load.Result;
        }

        internal Task Retire(){
            stop.Cancel();
            return load.ContinueWith(task=>{
                try{
                    if(task.Status==TaskStatus.RanToCompletion)task.Result.Dispose();
                    else if(task.IsFaulted)task.Exception.Handle(error=>true);
                }finally{stop.Dispose();}
            },TaskScheduler.Default);
        }
    }
}
