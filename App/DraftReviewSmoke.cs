using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MediaReviewer {
    static class DraftReviewSmoke {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static void Set(object target,string name,object value){target.GetType().GetField(name,Private).SetValue(target,value);}
        static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,Private).GetValue(target);}
        static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
        static void Wait(Func<bool> condition){var clock=Stopwatch.StartNew();while(!condition()){if(clock.Elapsed.TotalSeconds>30)throw new TimeoutException("AI review UI check timed out");Pump();Thread.Sleep(5);}}
        static FrameworkElement Find(DependencyObject root,string name){var element=root as FrameworkElement;if(element!=null && element.Name==name)return element;foreach(object child in LogicalTreeHelper.GetChildren(root)){var node=child as DependencyObject;if(node!=null){var match=Find(node,name);if(match!=null)return match;}}return null;}
        static T Control<T>(Window window,string name) where T:FrameworkElement{var value=Find(window,name) as T;Check(value!=null,"Missing AI review control: "+name);return value;}
        static void Save(Window window,string path){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
        static void Click(Window window,string name){Control<Button>(window,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        internal static int Run(string directory){
            Directory.CreateDirectory(directory);string path=Path.Combine(directory,"draft-ui.sqlite");
            using(var engine=ReviewerEngine.Open(path,true))Wait(()=>engine.Ready||engine.Faulted);
            var application=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            using(var controller=new ReviewerWindow(Program.ReadWindow(),false)){
                controller.Window.Show();controller.Open(path,false);
                Wait(()=>Field<bool>(controller,"gemmaSettingsLoaded"));
                // Exercise the settings UI without requiring optional model-weight files or inference.
                Set(controller,"gemmaAvailable",true);Set(controller,"useGemma",true);
                var dialog=controller.CreateDraftReviewWindow();dialog.Show();
                var presets=Control<ComboBox>(dialog,"GemmaReviewPreset");
                var detail=Control<ComboBox>(dialog,"GemmaScanDetail");
                Check(detail.Items.Count==3 && detail.SelectedIndex==0,"Gemma must default to Quick and offer three scan sizes.");
                detail.SelectedIndex=2;
                Check(presets.Items.Count==4 && presets.SelectedItem.ToString()=="More suggestions","New Gemma settings must show the supported named presets.");
                Check(!Control<StackPanel>(dialog,"MarqoReviewOptions").IsVisible,"Gemma must hide the continuous Marqo slider.");
                presets.SelectedIndex=2;detail.SelectedIndex=0;Click(dialog,"AiDraftRecommended");
                Check(presets.SelectedIndex==0 && detail.SelectedIndex==(DraftReview.RecommendedGemmaSide==1024 ? 2 : DraftReview.RecommendedGemmaSide==512 ? 1 : 0),"Recommended Gemma settings must restore the main preset and recommended detail without running a scan.");
                detail.SelectedIndex=2;
                foreach(var theme in ReviewerWindow.Themes){
                    controller.SetTheme(theme.Id);presets.SelectedIndex=0;Save(dialog,Path.Combine(directory,theme.Id+"-more.png"));
                    presets.SelectedIndex=3;Control<TextBox>(dialog,"GemmaCustomRules").Text="Firearms";Control<TextBox>(dialog,"GemmaIgnoreRules").Text="Toys and sports equipment";Save(dialog,Path.Combine(directory,theme.Id+"-custom.png"));
                }
                Control<TextBox>(dialog,"GemmaCustomRules").Text="";Check(!Control<Button>(dialog,"AiDraftSave").IsEnabled,"Empty Custom rules must not silently run the default policy.");
                string rules="Firearms",ignore="Toys and sports equipment";Control<TextBox>(dialog,"GemmaCustomRules").Text=rules;Control<TextBox>(dialog,"GemmaIgnoreRules").Text=ignore;
                presets.SelectedIndex=0;presets.SelectedIndex=3;Check(Control<TextBox>(dialog,"GemmaCustomRules").Text==rules,"Switching presets must retain the current custom draft.");
                bool helped=false;var closeHelp=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(30)};
                closeHelp.Tick+=(s,e)=>{var help=application.Windows.Cast<Window>().FirstOrDefault(w=>w.Name=="GemmaPromptHelpWindow");if(help==null)return;helped=true;Save(help,Path.Combine(directory,"custom-help.png"));closeHelp.Stop();help.Close();};closeHelp.Start();Click(dialog,"GemmaRulesHelp");closeHelp.Stop();Check(helped,"The help button must open the prompting guide.");
                Click(dialog,"AiDraftSave");Wait(()=>{using(var db=new SqliteConnection(path,true))return GemmaCriteria.Editable(Field<string>(controller,"gemmaCriteria"))==rules && GemmaCriteria.Editable(DraftReview.ReadCriteria(db))==rules && GemmaCriteria.IgnoreText(DraftReview.ReadCriteria(db))==ignore && DraftReview.Threshold(db,true)==97 && (string)db.Scalar("SELECT value FROM settings WHERE key='ai.draft.gemma.side'")=="1024";});
                dialog=controller.CreateDraftReviewWindow();dialog.Show();presets=Control<ComboBox>(dialog,"GemmaReviewPreset");Check(presets.SelectedIndex==3,"Saved Custom rules must reopen as Custom.");presets.SelectedIndex=0;Click(dialog,"AiDraftSave");
                Check(Control<ComboBox>(dialog,"GemmaScanDetail").SelectedIndex==2,"Thorough must reopen after saving.");
                Wait(()=>{using(var db=new SqliteConnection(path,true))return Field<string>(controller,"gemmaCriteria")==GemmaCriteria.Recommended && DraftReview.ReadCriteria(db)==GemmaCriteria.Recommended && DraftReview.Threshold(db,true)==55;});
                dialog=controller.CreateDraftReviewWindow();dialog.Show();presets=Control<ComboBox>(dialog,"GemmaReviewPreset");presets.SelectedIndex=3;Check(Control<TextBox>(dialog,"GemmaCustomRules").Text==rules && Control<TextBox>(dialog,"GemmaIgnoreRules").Text==ignore,"Custom rules must survive saving a built-in preset.");
                Control<ComboBox>(dialog,"DraftModel").SelectedIndex=0;Check(Control<StackPanel>(dialog,"MarqoReviewOptions").IsVisible && !Control<StackPanel>(dialog,"GemmaReviewOptions").IsVisible,"Marqo must keep its continuous score filter.");Control<Slider>(dialog,"MarqoSuggestionThreshold").Value=12;Save(dialog,Path.Combine(directory,"marqo.png"));Click(dialog,"AiDraftSave");
                Wait(()=>{using(var db=new SqliteConnection(path,true))return !Field<bool>(controller,"useGemma") && !DraftReview.PrefersGemma(db) && DraftReview.Threshold(db,false)==12 && GemmaCriteria.Editable(DraftReview.ReadCustomCriteria(db))==rules;});
                Set(controller,"useGemma",true);Set(controller,"gemmaAvailable",true);Set(controller,"gemmaThreshold",80);Set(controller,"gemmaCriteria","");
                dialog=controller.CreateDraftReviewWindow();dialog.Show();Check(Control<ComboBox>(dialog,"GemmaReviewPreset").SelectedItem.ToString()=="Saved filter","An uncommon saved threshold must remain available until changed.");Click(dialog,"AiDraftSave");Wait(()=>{using(var db=new SqliteConnection(path,true))return DraftReview.Threshold(db,true)==80;});
                dialog=controller.CreateDraftReviewWindow();dialog.Show();Control<ComboBox>(dialog,"GemmaReviewPreset").SelectedIndex=3;Control<TextBox>(dialog,"GemmaCustomRules").Text="Flag red objects. Ignore logos.";dialog.Close();
                using(var db=new SqliteConnection(path,true))Check(DraftReview.Threshold(db,true)==80 && DraftReview.ReadCriteria(db)=="" && GemmaCriteria.Editable(DraftReview.ReadCustomCriteria(db))==rules,"Cancel must leave saved rules and thresholds unchanged.");
                controller.Window.Close();Wait(()=>Field<bool>(controller,"allowClose"));
            }application.Shutdown();Console.WriteLine("AI presets, Custom rules, help, model switching, saved filters, cancellation and theme renders passed; model inference was not required.");return 0;
        }
    }
}
