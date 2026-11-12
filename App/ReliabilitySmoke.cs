using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace MediaReviewer {
    static class ReliabilitySmoke {
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        static void Until(Func<bool> condition,string message){var timer=Stopwatch.StartNew();while(!condition()){if(timer.ElapsedMilliseconds>15000)throw new Exception(message);Thread.Sleep(20);}}
        static UiResult Result(ReviewerEngine engine,string id){UiResult value=null;Until(()=>{UiResult next;while(engine.TryTakeResult(out next)){if(next.RequestId==id){value=next;return true;}}return false;},"No operation result: "+id);return value;}
        static ReviewerEngine Open(string directory,string name){var engine=ReviewerEngine.Open(Path.Combine(directory,name+".sqlite"),true);Until(()=>engine.Ready || engine.Faulted,"Database did not initialize");Check(engine.Ready,engine.Status.Error);return engine;}
        internal static int Run(string directory){
            Directory.CreateDirectory(directory);
            using(var engine=Open(directory,"operation-delivery")){
                int cleaned=0;
                string failed=engine.EnqueueOperation("Expected failure",(db,token,progress)=>{throw new IOException("Fixture unavailable");},cleanup:()=>Interlocked.Increment(ref cleaned));
                Check(!Result(engine,failed).Success,"An expected body error must not report completion");
                string next=engine.EnqueueOperation("Safe follow-up",(db,token,progress)=>"available");
                Check(Result(engine,next).Success,"A contained operation failure must permit the next operation");
                try{engine.WaitWrite(db=>{throw new ProtectedDetailsException();});}catch(ProtectedDetailsException){}
                Check(engine.Ready && !engine.Faulted,"Rejecting an ordinary edit of protected Evidence must leave the writer available after confirmed rollback");
                string publication=engine.EnqueueOperation("Original export",(db,token,progress)=>new OriginalReceipt {OutputPublished=true,Message=new string('x',9*1024*1024)},cleanup:()=>Interlocked.Increment(ref cleaned));
                UiResult outcome=Result(engine,publication);
                Check(!outcome.Success,"Broken delivery must not report successful completion");
                Check(outcome.Error.IndexOf("already published",StringComparison.OrdinalIgnoreCase)>=0,"An already published output must be disclosed before retrying");
                Until(()=>Volatile.Read(ref cleaned)==2,"Every operation must clear its owned resources after failed result delivery");
                Check(engine.GetOperationProgress(publication).State=="failed","Undelivered operation must have an explicit failed state");
                bool blocked=false;try{engine.EnqueueOperation("Unsafe retry",(db,token,progress)=>"unexpected");}catch(InvalidOperationException){blocked=true;}
                Check(blocked,"A broken result channel must explicitly stop accepting background operations");
                Check(Convert.ToInt64(engine.WaitWrite(db=>db.Scalar("SELECT 7")))==7,"A delivery failure must leave unrelated safe database work available");
            }
            using(var engine=Open(directory,"rollback")){
                engine.WaitWrite(db=>{db.Execute("CREATE TABLE reliability_fixture(value INTEGER)");return null;});
                bool failed=false;try{engine.WaitWrite(db=>{db.Execute("BEGIN IMMEDIATE");db.Execute("INSERT INTO reliability_fixture VALUES(1)");throw new IOException("Expected operation error");});}catch(IOException){failed=true;}
                Check(failed,"The operation error must reach its caller");
                Check(Convert.ToInt64(engine.WaitWrite(db=>db.Scalar("SELECT COUNT(*) FROM reliability_fixture")))==0,"Recovery must confirm rollback before accepting the next write");
                engine.WaitWrite(db=>{db.Execute("INSERT INTO reliability_fixture VALUES(2)");return null;});
            }
            using(var engine=Open(directory,"writer-fail-stop")){
                try{engine.WaitWrite(db=>{throw new NullReferenceException("Injected unexpected writer failure");});}catch(NullReferenceException){}
                Until(()=>engine.Faulted,"Unexpected writer state must fail-stop instead of silently continuing");
                bool rejected=false;try{engine.WaitWrite(db=>db.Scalar("SELECT 1"));}catch(InvalidOperationException){rejected=true;}
                Check(rejected,"A failed writer must reject new writes");
            }
            var boundary=typeof(ReviewerWindow).GetMethod("HandleUiFailure",BindingFlags.Instance|BindingFlags.NonPublic);
            Check(boundary!=null,"The UI timer and dispatcher need an explicit recovery boundary");
            using(var controller=new ReviewerWindow(Program.ReadWindow(),false)){
                using(var fixture=ReviewerEngine.Open(Path.Combine(directory,"ui-failure.sqlite"),true)){Until(()=>fixture.Ready||fixture.Faulted,"Fixture initialization failed");}controller.Open(Path.Combine(directory,"ui-failure.sqlite"),false);
                var field=typeof(ReviewerWindow).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic);
                var engine=(ReviewerEngine)field.GetValue(controller);Until(()=>engine.Ready || engine.Faulted,"UI database did not initialize");
                Check((bool)boundary.Invoke(controller,new object[]{new PresentationFailureException(new FileFormatException("Malformed preview"))}),"A contained discarded preview failure must recover");
                Check(engine.Ready && !engine.Faulted && controller.Window.IsEnabled,"A contained preview failure must leave safe review operations available");
                controller.Window.UnregisterName("ActivityText");
                typeof(ReviewerWindow).GetMethod("TickSafely",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);
                Check(engine.Faulted && !controller.Window.IsEnabled,"Unknown UI state must disable writes and controls");
                Until(()=>engine.Closed,"UI fail-stop must close owned database work");
            }
            using(var controller=new ReviewerWindow(Program.ReadWindow(),false)){
                using(var fixture=ReviewerEngine.Open(Path.Combine(directory,"ui-result-failure.sqlite"),true)){Until(()=>fixture.Ready||fixture.Faulted,"Fixture initialization failed");}controller.Open(Path.Combine(directory,"ui-result-failure.sqlite"),false);
                var engine=(ReviewerEngine)typeof(ReviewerWindow).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);Until(()=>engine.Ready || engine.Faulted,"Result UI database did not initialize");
                var callbacks=(Dictionary<string,Action<UiResult>>)typeof(ReviewerWindow).GetField("pending",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
                callbacks.Add("fault-result",r=>{throw new NullReferenceException("Injected unknown completion callback failure");});
                typeof(ReviewerWindow).GetMethod("DispatchResult",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{new UiResult {RequestId="fault-result",Kind="Fixture",Success=true}});
                Check(engine.Faulted && !controller.Window.IsEnabled,"An unknown completion callback failure must fail-stop instead of only showing an error");
                Until(()=>engine.Closed,"Result failure must close owned database work");
            }
            using(var controller=new ReviewerWindow(Program.ReadWindow(),false)){
                using(var fixture=ReviewerEngine.Open(Path.Combine(directory,"ui-action-failure.sqlite"),true)){Until(()=>fixture.Ready||fixture.Faulted,"Fixture initialization failed");}controller.Open(Path.Combine(directory,"ui-action-failure.sqlite"),false);
                var engine=(ReviewerEngine)typeof(ReviewerWindow).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);Until(()=>engine.Ready || engine.Faulted,"Action UI database did not initialize");
                var guard=typeof(ReviewerWindow).GetMethod("Guard",BindingFlags.Instance|BindingFlags.NonPublic);
                guard.Invoke(controller,new object[]{new Action(()=>{throw new ArgumentException("Invalid selected action");})});
                Check(engine.Ready && !engine.Faulted,"Ordinary action validation must leave safe operations available");
                guard.Invoke(controller,new object[]{new Action(()=>{throw new NullReferenceException("Injected unknown button failure");})});
                Check(engine.Faulted && !controller.Window.IsEnabled,"Unknown action state must fail-stop instead of only showing an error");
                Until(()=>engine.Closed,"Action failure must close owned database work");
            }
            Console.WriteLine("Reliability passed: operation errors, published-output delivery failure, cleanup, confirmed rollback, unknown writer and UI fail-stop.");return 0;
        }
    }
}
