using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7.2")]
[assembly:AssemblyTitle("Media Compliance Reviewer for Windows")]
[assembly:AssemblyCompany("IT1 Charlie McPherson")]

namespace MediaReviewer {
    static class Program {
        [DllImport("kernel32.dll")] static extern IntPtr GetCommandLineW();
        [DllImport("shell32.dll")] static extern IntPtr CommandLineToArgvW(IntPtr command,out int count);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);

        static string[] Arguments() {
            int count;IntPtr args=CommandLineToArgvW(GetCommandLineW(),out count);
            if(args==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
            try {return Enumerable.Range(1,count-1).Select(i=>Marshal.PtrToStringUni(Marshal.ReadIntPtr(args,i*IntPtr.Size))).ToArray();}
            finally{LocalFree(args);}
        }

        internal static Window ReadWindow() {
            var assembly=Assembly.GetExecutingAssembly();
            Window window;
            using(var markup=assembly.GetManifestResourceStream("MainWindow.xaml"))window=(Window)XamlReader.Load(markup);
            using(var icon=assembly.GetManifestResourceStream("Reviewer.ico")) {
                var frames=new IconBitmapDecoder(icon,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames;
                window.Icon=frames.FirstOrDefault(f=>f.PixelWidth==32)??frames[0];
            }
            return window;
        }

        [STAThread] static int Main() {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
            return Run();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int Run() {
            string[] args=Arguments();
            string mode=args.FirstOrDefault();
            if(mode=="--verify-symbols" && args.Length==1){try{ManagedSmoke.VerifySymbols();Console.WriteLine("Matching native-host symbols include source lines.");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
            bool headless=mode=="--verify-file-properties" || mode=="--verify-security" || CommandLine.IsHeadless(args) || mode=="--verify-stego-policy" || mode=="--verify-review-ui" || mode=="--verify-preview-memory" || mode=="--verify-reliability" || mode=="--verify-workflow" || mode=="--verify-companion" || mode=="--verify-stego" || mode=="--verify-stego-queue" || mode=="--verify-gallery" || mode=="--verify-stego-interface" || mode=="--verify-stego-notes" || mode=="--stego-source-worker";
            string scratch=Path.Combine(Path.GetTempPath(),"MediaComplianceReviewer-"+Guid.NewGuid().ToString("N"));
            try {
                if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)throw new InvalidOperationException("The desktop entry point needs an STA thread.");
                Native.Initialize(scratch);
                if(mode=="--verify-file-properties" && args.Length==2)return FilePropertiesSmoke.Run(args[1]);
                if(mode=="--verify-security" && args.Length==2)return SecuritySmoke.Run(args[1]);
                if(mode=="--stego-source-worker" && args.Length==1)return StegoSource.Worker();
                if(mode=="--preview-worker")return DecoderProgram.Run();
                if(mode=="--draft-worker" && (args.Length==1 || args.Length==2 && (args[1]=="gemma" || args[1]=="gemma-vulkan")))return DraftModelWorker.Run(args.Length==2,args.Length==2 && args[1]=="gemma-vulkan");
                if(mode=="--playback-worker")return PlaybackProxy.Worker();
                MediaScratch.QueueRecovery();
                if(mode=="--verify-reliability" && args.Length==2)return ReliabilitySmoke.Run(args[1]);
                if(mode=="--verify-stego-policy" && args.Length==2)return StegoPolicySmoke.Run(args[1]);
                if(mode=="--verify-preview-memory" && (args.Length==2 || args.Length==3))return PreviewMemorySmoke.Run(args[1],args.Length==3 ? args[2] : null);
                if(mode=="--verify-review-ui" && args.Length==2)return ReviewUiSmoke.Run(args[1]);
                if(mode=="--verify-draft-ui" && args.Length==2)return DraftReviewSmoke.Run(args[1]);
                if(mode=="--verify-stego-notes" && args.Length==2)return StegoQueueSmoke.NotesRun(args[1]);
                if(mode=="--verify-stego-interface" && args.Length==2)return StegoQueueSmoke.InterfaceRun(args[1]);
                if(mode=="--verify-gallery" && args.Length==2)return GalleryAnimationSmoke.Run(args[1]);
                if(mode=="--verify-stego-queue" && args.Length==2)return StegoQueueSmoke.Run(args[1]);
                if(mode=="--verify-stego" && args.Length==3)return StegoSmoke.Run(args[1],args[2]);
                if(mode=="--verify-companion" && args.Length==2)return CompanionSmoke.Run(args[1]);
                if(mode=="--verify-workflow" && args.Length==3)return WorkflowSmoke.Run(args[1],args[2]);
                if(headless)return CommandLine.Run(args);
                if(mode=="--self-test" && (args.Length==2 || args.Length==3))return Smoke.Run(args[1],args.Length==3 ? args[2] : null);
                if(mode=="--verify-previews" && args.Length>=2)return Smoke.PreviewFiles(args.Skip(1));
                if(mode=="--verify-import" && args.Length==3)return Smoke.Import(args[1],args[2]);
                if(mode!=null && mode!="--window-smoke" && ((mode!="--database" && mode!="--open-evidence-package") || args.Length!=2))throw new ArgumentException("Use --database with a local SQLite path, --open-evidence-package with a package path, or launch without arguments.");
                var app=new Application();
                using(var controller=new ReviewerWindow(ReadWindow(),mode!="--window-smoke")) {
                    app.DispatcherUnhandledException+=(s,e)=>{try{e.Handled=controller.HandleUiFailure(e.Exception);}catch(Exception boundaryError){FailureDiagnostics.Record("Dispatcher fail-stop",boundaryError);e.Handled=false;}};
                    if(mode==null)controller.Window.Loaded+=(s,e)=>controller.Window.Dispatcher.BeginInvoke(new Action(()=>controller.ShowStartupCatalog()));
                    if(mode=="--database")controller.Open(args[1],false);
                    if(mode=="--open-evidence-package")controller.Window.Loaded+=(s,e)=>controller.Window.Dispatcher.BeginInvoke(new Action(()=>{
                        try{controller.OpenPackage(args[1]);}catch(Exception error){MessageBox.Show(controller.Window,error.Message,"Could not open Evidence package");}
                        finally{controller.Window.Close();}
                    }));
                    if(mode=="--window-smoke") {
                        if(args.Length!=2)throw new ArgumentException("Window smoke test needs a fixture database.");
                        controller.Window.Width=1024;controller.Window.Height=768;
                        controller.Window.Loaded+=(s,e)=>controller.Window.Dispatcher.BeginInvoke(new Action(()=>{
                            try{controller.ShowStartupCatalog(Smoke.CheckStartupPrompt);Smoke.CheckCatalogPath(args[1]);controller.Open(args[1],false);}
                            catch(Exception error){Console.Error.WriteLine(error);controller.Window.Close();}
                        }));
                        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
                        var elapsed=System.Diagnostics.Stopwatch.StartNew();
                        bool rendered=false;
                        int reviewsStarted=0;
                        timer.Tick+=(s,e)=>{
                            if(reviewsStarted!=0) {
                                var message=((System.Windows.Controls.TextBlock)controller.Window.FindName("MessageText")).Text;
                                if(message!="Review saved." && elapsed.Elapsed<TimeSpan.FromSeconds(12))return;
                                timer.Stop();rendered=message=="Review saved.";
                                if(!rendered)Console.Error.WriteLine("Quick Review did not commit: "+message);
                                if(rendered && reviewsStarted==1) {
                                    try{
                                        Smoke.MarkQuickNonCompliant(controller.Window);
                                        reviewsStarted++;elapsed.Restart();timer.Start();return;
                                    }catch(Exception error){rendered=false;Console.Error.WriteLine(error);}
                                }
                                controller.Window.Close();return;
                            }
                            if(!controller.HasRenderedPreview && elapsed.Elapsed<TimeSpan.FromSeconds(12))return;
                            rendered=controller.HasRenderedPreview;timer.Stop();
                            try{if(rendered){
                                string output=Path.GetDirectoryName(args[1]);
                                Smoke.CheckShell(controller.Window,Path.Combine(output,"gallery.png"));
                                controller.Window.Width=1280;controller.Window.UpdateLayout();
                                if(controller.Window.ActualWidth>=1160) {
                                    Smoke.CheckWideShell(controller.Window);
                                    Smoke.SaveGallery(controller.Window,Path.Combine(output,"workspace-dark.png"));
                                }
                                Smoke.CheckQuickNavigationAndMarkCompliant(controller.Window,controller.ApplyPage);
                                reviewsStarted++;elapsed.Restart();timer.Start();return;
                            }}
                            catch(Exception error){rendered=false;Console.Error.WriteLine(error);}
                            controller.Window.Close();
                        };
                        timer.Start();app.Run(controller.Window);
                        if(rendered)Smoke.CheckSavedQuickReviews(args[1]);
                        return rendered ? 0 : 1;
                    }
                    return app.Run(controller.Window);
                }
            }catch(Exception error) {
                if(headless || mode=="--verify-draft-ui" || mode=="--self-test" || mode=="--window-smoke" || mode=="--preview-worker" || mode=="--draft-worker" || mode=="--playback-worker" || mode=="--verify-previews" || mode=="--verify-import")Console.Error.WriteLine(error);
                else MessageBox.Show(error.GetBaseException().Message,"Media Compliance Reviewer could not start",MessageBoxButton.OK,MessageBoxImage.Error);
                return 1;
            }finally {
                if(mode!="--preview-worker" && mode!="--draft-worker") {
                    Preview.Shutdown();
                    MediaScratch.Shutdown();
                }
            }
        }
    }
}
