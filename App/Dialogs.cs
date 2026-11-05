using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace MediaReviewer {
    sealed partial class ReviewerWindow {
        Window protectedWindow;
        bool offeredImport;

        string ChooseFolder(string title,string current) {
            using(var dialog=new System.Windows.Forms.FolderBrowserDialog {Description=title,SelectedPath=current??"",ShowNewFolderButton=false}) {
                var owner=new System.Windows.Interop.WindowInteropHelper(Window).Handle;
                return dialog.ShowDialog(new FormsOwner(owner))==System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
            }
        }
        sealed class FormsOwner:System.Windows.Forms.IWin32Window {public IntPtr Handle{get;private set;} internal FormsOwner(IntPtr handle){Handle=handle;}}
        string OpenFile(string filter){var picker=new OpenFileDialog {Filter=filter};return picker.ShowDialog(Window)==true ? picker.FileName : null;}
        string SaveFile(string filter,string name){var picker=new SaveFileDialog {Filter=filter,FileName=name,OverwritePrompt=false};return picker.ShowDialog(Window)==true ? picker.FileName : null;}

        Window Dialog(string title,FrameworkElement body,double width=680,double height=480) {
            var dialog=new Window {Title=title,Owner=Window,WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Width=width,Height=height,MinWidth=Math.Min(420,width),MinHeight=Math.Min(240,height),FontFamily=Window.FontFamily,
                FontSize=Window.FontSize,Background=Window.Background,Foreground=Window.Foreground,Content=body};
            dialog.Resources.MergedDictionaries.Add(Window.Resources);
            dialog.SetResourceReference(Control.BackgroundProperty,"PageBrush");
            dialog.SetResourceReference(Control.ForegroundProperty,"TextBrush");
            return dialog;
        }
        static Button ActionButton(string label,Action action) {
            var b=new Button {Content=label,MinHeight=30,Padding=new Thickness(10,4,10,4),Margin=new Thickness(3)};
            b.Click+=(s,e)=>action();return b;
        }
        static TextBlock Label(string text){return new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)};}
        static WrapPanel Bar(){return new WrapPanel {HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,8,0,0)};}
        static void Dock(DockPanel parent,UIElement child,Dock side){DockPanel.SetDock(child,side);parent.Children.Add(child);}
        static TextBox Editor(string value,int limit=16384){return new TextBox {Text=value??"",MaxLength=limit,Padding=new Thickness(6),VerticalContentAlignment=VerticalAlignment.Center};}
        void TrackDatabaseWindow(Window dialog){databaseWindows.Add(dialog);dialog.Closed+=(s,e)=>databaseWindows.Remove(dialog);}
        void CloseDatabaseWindows(){foreach(Window dialog in databaseWindows.ToArray())dialog.Close();}

        internal void ShowStartupCatalog(Action<Window> beforeShow=null) {
            var panel=new StackPanel {Margin=new Thickness(20)};
            var dialog=Dialog("Open or create a catalog",panel,510,240);
            dialog.Icon=Window.Icon;dialog.ShowInTaskbar=false;dialog.ResizeMode=ResizeMode.NoResize;
            panel.Children.Add(new TextBlock {Text="Media Compliance Reviewer",FontSize=21,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
            panel.Children.Add(Label("Select an existing catalog to continue, or enter a new filename to create one."));
            var actions=Bar();panel.Children.Add(actions);
            var open=ActionButton("Open or create catalog…",()=>dialog.DialogResult=true);
            open.Name="StartupCatalogButton";open.IsDefault=true;actions.Children.Add(open);
            var later=ActionButton("Later",dialog.Close);later.Name="StartupLaterButton";later.IsCancel=true;actions.Children.Add(later);
            if(beforeShow!=null)beforeShow(dialog);
            if(dialog.ShowDialog()==true)Guard(ChooseDatabase);
        }

        void About() {
            var panel=new DockPanel {Margin=new Thickness(18)};var dialog=Dialog("About Media Compliance Reviewer",panel,720,560);
            var actions=Bar();actions.Children.Add(ActionButton("Close",dialog.Close));Dock(panel,actions,System.Windows.Controls.Dock.Bottom);
            var text=new StackPanel();panel.Children.Add(new ScrollViewer {Content=text,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            text.Children.Add(Label("Media Compliance Reviewer for Windows\n.NET development build · "+Product.DotNetVersion+" · "+Product.DotNetDate+"\nMade by "+Product.Author));
            using(var resource=typeof(ReviewerWindow).Assembly.GetManifestResourceStream("COPYING.LGPLv2.1"))
            using(var reader=new StreamReader(resource)) {
                text.Children.Add(new Expander {Header="SQLite "+Native.SqliteVersion+" · Public domain",Content=Label("SQLite is statically linked. Source: https://www.sqlite.org/2026/sqlite-amalgamation-3540100.zip")});
                text.Children.Add(new Expander {Header="FFmpeg 9.0.2 · LGPL 2.1 or later",Content=Label("FFmpeg is statically linked. Corresponding source: https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz\n\n"+reader.ReadToEnd())});
            }
            using(var resource=typeof(ReviewerWindow).Assembly.GetManifestResourceStream("COPYING.dav1d"))
            using(var reader=new StreamReader(resource))text.Children.Add(new Expander {Header="dav1d 1.5.4 · BSD 2-Clause",Content=Label(reader.ReadToEnd())});
            text.Children.Add(new Expander {Header="Microsoft Windows components",Content=Label("WPF, Windows Imaging Component and .NET Framework are supplied by Windows.")});
            dialog.Show();
        }
        void ShowText(string title,string value) {
            var content=new DockPanel {Margin=new Thickness(16)};var dialog=Dialog(title,content);
            var actions=Bar();Dock(content,actions,System.Windows.Controls.Dock.Bottom);
            if(title=="Log")actions.Children.Add(ActionButton("Save text…",()=>Guard(()=>{string path=SaveFile("Text file (*.txt)|*.txt","Media Review Log.txt");if(path!=null)using(var stream=new FileStream(path,FileMode.CreateNew))using(var writer=new StreamWriter(stream))writer.Write(value);})));
            actions.Children.Add(ActionButton("Close",dialog.Close));
            content.Children.Add(new TextBox {Text=value??"",IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(6)});
            dialog.Show();
        }

        void OfferImport() {
            if(offeredImport)return;offeredImport=true;
            string source=engine.DatabasePath;
            if(MessageBox.Show(Window,"A final HTML database was detected. Create a Windows copy for reviewing?","Open HTML database",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            string destination=SaveFile("SQLite database (*.sqlite)|*.sqlite",Path.GetFileNameWithoutExtension(source)+"-Windows.sqlite");
            if(destination==null)return;
            var old=engine;engine=null;closingDatabase=old;old.CloseAsync();
            var wait=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
            wait.Tick+=(s,e)=>{if(old.Closed){wait.Stop();closingDatabase=null;if(!closing)Guard(()=>StartOpen(destination,true,source));}};wait.Start();
        }

        void ChooseOptions(){
            var panel=new StackPanel {Margin=new Thickness(16)};var dialog=Dialog("Scan options",panel,550,355);
            var appData=new CheckBox {Content="Include Application Data and AppData",IsChecked=options.IncludeApplicationData,Margin=new Thickness(4)};
            var root=new CheckBox {Content="Include root-level media files",IsChecked=options.IncludeRootFiles,Margin=new Thickness(4)};
            var zip=new CheckBox {Content="Scan supported ZIP archives",IsChecked=options.ScanArchives,Margin=new Thickness(4)};
            var sniff=new CheckBox {Content="Detect renamed media by file header",IsChecked=options.SniffRenamed,Margin=new Thickness(4),ToolTip="Checks a small header on ordinary files with unknown extensions. It can increase reads on a network share."};
            var full=new CheckBox {Content="Read every selected file for full SHA-256",IsChecked=options.FullHashScans,Margin=new Thickness(4)};
            panel.Children.Add(Label("Choose which files to scan and how to verify them. Deep always includes AppData."));
            foreach(var toggle in new[]{appData,root,zip,sniff,full})panel.Children.Add(toggle);
            var actions=Bar();panel.Children.Add(actions);
            actions.Children.Add(ActionButton("Apply",()=>{options.IncludeApplicationData=appData.IsChecked==true;options.IncludeRootFiles=root.IsChecked==true;options.ScanArchives=zip.IsChecked==true;options.SniffRenamed=sniff.IsChecked==true;options.FullHashScans=full.IsChecked==true;dialog.Close();}));
            actions.Children.Add(ActionButton("Cancel",dialog.Close));dialog.ShowDialog();
        }

        void ChooseFormats(){
            var content=new DockPanel {Margin=new Thickness(16)};var dialog=Dialog("File types",content,530,520);var actions=Bar();
            Dock(content,actions,System.Windows.Controls.Dock.Bottom);
            var list=new StackPanel();
            var checks=Formats.Families.Select(f=>new CheckBox {Content=f.Label+"  ("+String.Join(", ",f.Extensions)+")",Tag=f.Key,
                IsChecked=options.Families.Contains(f.Key),Margin=new Thickness(3,4,3,4)}).ToArray();
            foreach(var check in checks)list.Children.Add(check);
            actions.Children.Add(ActionButton("Apply",()=>{var families=checks.Where(c=>c.IsChecked==true).Select(c=>(string)c.Tag).ToArray();if(families.Length==0)return;options.Families=families;Combo("ModeBox").SelectedIndex=2;dialog.Close();}));
            actions.Children.Add(ActionButton("Cancel",dialog.Close));content.Children.Add(new ScrollViewer {Content=list});dialog.ShowDialog();
        }

        byte[] Password(string title,string prompt,bool confirm){
            var panel=new StackPanel {Margin=new Thickness(18)};var dialog=Dialog(title,panel,535,confirm ? 315 : 250);
            panel.Children.Add(Label(prompt));
            var input=new PasswordBox {Height=36,Padding=new Thickness(6),VerticalContentAlignment=VerticalAlignment.Center,MaxLength=16384};panel.Children.Add(input);
            var again=new PasswordBox {Height=36,Padding=new Thickness(6),VerticalContentAlignment=VerticalAlignment.Center,MaxLength=16384};
            if(confirm){panel.Children.Add(Label("Confirm password"));panel.Children.Add(again);}
            var actions=Bar();panel.Children.Add(actions);byte[] value=null;
            var ok=ActionButton(confirm ? "Set password" : "Unlock",()=>{
                if(input.Password.Length==0 || confirm && input.Password!=again.Password)return;
                value=Encoding.UTF8.GetBytes(input.Password);dialog.DialogResult=true;
            });
            ok.IsDefault=true;actions.Children.Add(ok);var cancel=ActionButton("Cancel",dialog.Close);cancel.IsCancel=true;actions.Children.Add(cancel);
            dialog.Loaded+=(s,e)=>input.Focus();try{dialog.ShowDialog();return value;}finally{input.Clear();again.Clear();}
        }

        void Unlock(Action complete,Action abandoned=null) {
            byte[] password=null;
            try {
                bool create=!engine.VaultExists;
                password=Password(create ? "Set originals password" : "Unlock stored originals",
                    create ? "Choose a password for encrypted originals. Keep it somewhere safe." : "Enter the current originals password. Imported HTML Evidence initially uses its saved HTML password.",create);
                if(password==null){if(abandoned!=null)abandoned();return;}
                Work(engine.RequestVaultUnlock(password,create),"Unlock originals",r=>{
                    if(r.Success){if(complete!=null)complete();}
                    else if(abandoned!=null)abandoned();
                },true);
            }catch{
                if(password!=null)Array.Clear(password,0,password.Length);
                if(abandoned!=null)abandoned();
                throw;
            }
        }

        void RetainOriginals() {
            var selected=Selected();
            if(selected.Any(c=>c.Record.Status!="NON_COMPLIANT"))throw new InvalidOperationException("Select Non-compliant items to retain originals without changing their saved decisions.");
            if(!engine.VaultUnlocked){Unlock(RetainOriginals);return;}
            Work(engine.RequestRetainOriginals(selected.Select(c=>c.Record.GroupId).ToArray(),selected.ToDictionary(c=>c.Record.GroupId,c=>c.Record.ReviewRevision),Id()),"Retain originals");
        }

        void Original(MediaRow row) {
            if(!engine.VaultUnlocked){Unlock(()=>Original(row));return;}
            Work(engine.RequestEvidenceInspect(row.GroupId),"Verify stored original",r=>{
                if(protectedWindow!=null)protectedWindow.Close();
                var content=new DockPanel {Margin=new Thickness(16)};
                var dialog=Dialog("Stored original",content,740,560);protectedWindow=dialog;
                dialog.Closed+=(s,e)=>{content.Children.Clear();if(protectedWindow==dialog)protectedWindow=null;};
                var metadata=Editor(EvidencePackageSession.Describe(r.Evidence));metadata.IsReadOnly=true;metadata.TextWrapping=TextWrapping.Wrap;
                metadata.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;metadata.MaxHeight=180;
                Dock(content,metadata,System.Windows.Controls.Dock.Top);
                var actions=Bar();Dock(content,actions,System.Windows.Controls.Dock.Bottom);
                actions.Children.Add(ActionButton("Export original…",()=>Guard(()=>ExportOriginal(row))));actions.Children.Add(ActionButton("Close",dialog.Close));
                if(r.Evidence.PreviewImage!=null)content.Children.Add(new Image {Source=r.Evidence.PreviewImage,Stretch=Stretch.Uniform});
                else content.Children.Add(Label(r.Evidence.PreviewError??"No stored preview available."));
                dialog.Show();
            });
        }
        void ExportOriginal(MediaRow row) {
            if(!engine.VaultUnlocked){Unlock(()=>ExportOriginal(row));return;}
            string path=SaveFile("All files (*.*)|*.*",row.Name);if(path!=null)Work(engine.RequestEvidenceOriginalExport(row.GroupId,path,Id()),"Export verified original");
        }
        void Verify(MediaRow row){
            Work(engine.RequestFullVerification(row.GroupId,null),"Verify full",r=>{
                var preview=r.IdentityPreview;if(preview==null)return;
                IdentityResults(preview);
            });
        }

        void Maintenance() {
            var menu=new ContextMenu();
            Menu(menu,"Portable backup…",()=>{string path=SaveFile("SQLite database (*.sqlite)|*.sqlite","Media Review Backup.sqlite");if(path!=null)Work(engine.RequestBackup(path),"Backup");});
            Menu(menu,"Check database integrity",()=>Work(engine.RequestIntegrity(),"Integrity check"));
            Menu(menu,"Retention limits…",()=>Work(engine.RequestOriginalLimits(),"Read retention limits",r=>RetentionLimits(EvidenceFormat.Json().Deserialize<OriginalStorageLimits>(r.Value))));
            Menu(menu,"Change originals password…",ChangePassword);
            Menu(menu,"Use current source path as default",()=>ChangeDefaultSource(Box("SourceBox").Text.Trim()));
            Menu(menu,"Clear default source path",()=>ChangeDefaultSource(""));
            Menu(menu,"Sources / reconnect…",ManageSources);
            Menu(menu,"Open catalog folder",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetDirectoryName(engine.DatabasePath)){UseShellExecute=true}));
            Menu(menu,"Reconnect all sources…",()=>{
                string path=PromptSource();
                if(path!=null)Work(engine.RequestReconnectAll(path),"Reconnect all sources");
            });
            Menu(menu,"Resume source reconnection",()=>Queue(engine.RequestReconnectAllStatus(),r=>{
                if(!r.Success){Message(r.Error);return;}
                if(String.IsNullOrEmpty(r.Reconnection.Id)){Message("No saved reconnection.");return;}
                Work(engine.RequestResumeReconnectAll(r.Reconnection.Id),"Resume reconnection");
            }));
            Menu(menu,"Discard unfinished source reconnection",()=>Queue(engine.RequestReconnectAllStatus(),r=>{
                if(!r.Success){Message(r.Error);return;}
                if(String.IsNullOrEmpty(r.Reconnection.Id)){Message("No saved reconnection.");return;}
                if(MessageBox.Show(Window,"Discard the unfinished source checks? Committed connections are preserved.","Discard source checks",MessageBoxButton.YesNo)==MessageBoxResult.Yes)
                    Work(engine.RequestDiscardReconnectAll(r.Reconnection.Id),"Discard source checks");
            }));
            Menu(menu,"Export review report…",Report);
            Menu(menu,"Export selected protected Evidence package…",()=>{
                if(!engine.VaultUnlocked){Unlock(ExportPackage);return;}ExportPackage();
            });
            Menu(menu,"Clean up unfinished originals",()=>{if(MessageBox.Show(Window,"Discard hidden unfinished original captures?","Clean up",MessageBoxButton.YesNo)==MessageBoxResult.Yes)Work(engine.RequestOriginalCleanup(),"Clean up originals");});
            menu.PlacementTarget=Button("MaintenanceButton");menu.IsOpen=true;
        }
        string PromptSource() {
            var body=new StackPanel {Margin=new Thickness(16)};var dialog=Dialog("Reconnect all sources",body,650,285);
            body.Children.Add(Label("Enter the actual media root. You can use a UNC share, mapped drive, or the local folder on the file server."));
            var path=Editor(Box("SourceBox").Text,32767);body.Children.Add(path);
            var actions=Bar();body.Children.Add(actions);
            actions.Children.Add(ActionButton("Browse…",()=>{string selected=ChooseFolder("Choose the replacement root",path.Text);if(selected!=null)path.Text=selected;}));
            var ok=ActionButton("Reconnect",()=>{if(!String.IsNullOrWhiteSpace(path.Text))dialog.DialogResult=true;});ok.IsDefault=true;actions.Children.Add(ok);
            actions.Children.Add(ActionButton("Cancel",dialog.Close));
            return dialog.ShowDialog()==true ? path.Text.Trim() : null;
        }
        void RetentionLimits(OriginalStorageLimits limits) {
            var body=new StackPanel {Margin=new Thickness(16)};var dialog=Dialog("Retention limits",body,525,355);
            body.Children.Add(Label("Use 0 for no fixed cap. Windows free space is checked for every capture."));
            body.Children.Add(Label("Database cap in GB"));var database=Editor((limits.DatabaseBytes/1000000000m).ToString(CultureInfo.CurrentCulture),24);body.Children.Add(database);
            body.Children.Add(Label("Individual original cap in GB"));var original=Editor((limits.FileBytes/1000000000m).ToString(CultureInfo.CurrentCulture),24);body.Children.Add(original);
            var feedback=Label("");body.Children.Add(feedback);var actions=Bar();body.Children.Add(actions);
            actions.Children.Add(ActionButton("Save",()=>{
                double db,file;if(!Double.TryParse(database.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out db) || !Double.TryParse(original.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out file)){feedback.Text="Enter a valid number for each limit.";return;}
                try{Work(engine.RequestOriginalLimits(db,file),"Save retention limits");dialog.Close();}catch(Exception error){feedback.Text=error.Message;}
            }));
            actions.Children.Add(ActionButton("Cancel",dialog.Close));dialog.ShowDialog();
        }

        void ChangePassword() {
            if(!engine.VaultExists)throw new InvalidOperationException("Create an originals vault first.");
            byte[] current=Password("Current originals password","Enter the current originals password.",false);
            if(current==null)return;
            byte[] replacement=null;bool submitted=false;
            try {
                replacement=Password("New originals password","Choose a new password. Earlier copies keep the password they were saved with.",true);
                if(replacement==null)return;
                if(protectedWindow!=null)protectedWindow.Close();
                Work(engine.RequestVaultPasswordChange(current,replacement,Id()),"Change originals password");submitted=true;
            }finally{if(!submitted){Array.Clear(current,0,current.Length);if(replacement!=null)Array.Clear(replacement,0,replacement.Length);}}
        }
        void ChangeDefaultSource(string path) {
            if(savedPreferences==null || savedPreferences.ReadOnly)throw new InvalidOperationException("Load writable preferences before changing the default source.");
            string previous=options.DefaultSourceRoot;
            options.DefaultSourceRoot=String.IsNullOrEmpty(path) ? "" : Paths.Source(path);
            try {
                string request=SavePreferences(engine);string signature=options.Signature;
                if(request==null)return;
                Queue(request,r=>{if(r.Success){preferencesSignature=signature;Message("Default source saved for this database.");}else{options.DefaultSourceRoot=previous;Message(r.Error);}});
                Message("Saving default source…");
            }catch{options.DefaultSourceRoot=previous;throw;}
        }
        void Menu(ContextMenu menu,string label,Action action){var item=new MenuItem {Header=label};item.Click+=(s,e)=>Guard(action);menu.Items.Add(item);}
        void ExportPackage(){
            var chosen=Selected();if(chosen.Length==0 || chosen.Any(c=>c.Record.Status!="EVIDENCE"))throw new InvalidOperationException("Select Evidence cards to package.");
            string path=SaveFile("Evidence package (*.mcrevidence)|*.mcrevidence","Evidence.mcrevidence");
            if(path!=null)Work(engine.RequestEvidencePackageExport(chosen.Select(c=>c.Record.GroupId).ToArray(),path,Id()),"Export Evidence package");
        }
    }
}
