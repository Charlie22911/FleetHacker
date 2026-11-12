using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
namespace MediaReviewer {
    sealed partial class ReviewerWindow {
        internal Window CreateWelcomeWindow(){
            var panel=new StackPanel {Margin=new Thickness(26)};
            var dialog=Dialog("Welcome to Media Review",panel,600,375);
            dialog.Icon=Window.Icon;dialog.ShowInTaskbar=false;
            panel.Children.Add(new TextBlock {Text="Welcome to Media Review",FontSize=25,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
            panel.Children.Add(Label("Review photos and videos, save decisions, and keep evidence protected."));
            panel.Children.Add(Label("Create a database to get started, or open one you already use. Your database holds saved previews, notes, and review decisions."));
            var feedback=Label("New databases use encryption and separate database and Supervisor passwords.");feedback.Name="WelcomeFeedback";panel.Children.Add(feedback);
            var actions=Bar();panel.Children.Add(actions);
            Action<Action> attempt=action=>{try{action();}catch(Exception error){feedback.Text=FailureDiagnostics.Brief(error);}};
            var create=ActionButton("Create database…",()=>attempt(()=>{string path=SaveFile("Media Review database (*.sqlite)|*.sqlite","Media Review.sqlite");if(path!=null)Open(path,true);}));
            create.Name="StartupCreateButton";create.IsDefault=true;actions.Children.Add(create);
            var open=ActionButton("Open database…",()=>attempt(()=>{var picker=new OpenFileDialog {Title="Open database",Filter="Media Review database (*.sqlite;*.db)|*.sqlite;*.db|All files (*.*)|*.*",CheckFileExists=true};if(picker.ShowDialog(dialog)==true)Open(picker.FileName,false);}));
            open.Name="StartupCatalogButton";actions.Children.Add(open);
            var exit=ActionButton("Exit",dialog.Close);exit.Name="StartupExitButton";exit.IsCancel=true;actions.Children.Add(exit);
            var monitor=new DispatcherTimer(DispatcherPriority.Background,dialog.Dispatcher){Interval=TimeSpan.FromMilliseconds(100)};
            monitor.Tick+=(s,e)=>{
                bool busy=engine!=null && !engine.Ready && !engine.Faulted || closingDatabase!=null;
                create.IsEnabled=open.IsEnabled=!busy;
                if(Ready){dialog.DialogResult=true;return;}
                if(engine!=null)feedback.Text=engine.Faulted ? "Could not open database. "+engine.Status.Error : engine.Status.Stage;
            };
            dialog.Loaded+=(s,e)=>monitor.Start();dialog.Closed+=(s,e)=>monitor.Stop();return dialog;
        }
        DatabaseCredentials UnlockDatabase(string path){
            byte[] bytes=Password("Unlock database","Enter the database password.",false);if(bytes==null)return null;
            var access=new DatabaseCredentials(bytes);try{DatabaseProtection.Validate(path,access.Database);return access;}catch{access.Dispose();throw;}
        }
        internal Window CreateDatabaseSetupWindow(bool supervisorRequired,Action<DatabaseCredentials> accept){
            var panel=new StackPanel {Margin=new Thickness(24)};
            var dialog=Dialog("Protect your database",new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},590,supervisorRequired ? 610 : 390);
            panel.Children.Add(new TextBlock {Text="Protect your database",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
            panel.Children.Add(Label("Use at least 12 characters for each new password. Keep them safe; forgotten passwords cannot be recovered."));
            Func<string,string,PasswordBox> field=(label,name)=>{panel.Children.Add(Label(label));var input=new PasswordBox {Name=name,MinHeight=34,Padding=new Thickness(6),MaxLength=16384};panel.Children.Add(input);return input;};
            var database=field("Database password","SetupDatabasePassword");var confirm=field("Confirm database password","SetupDatabaseConfirm");
            PasswordBox supervisor=null,again=null;
            if(supervisorRequired){panel.Children.Add(Label("The Supervisor password unlocks retained originals, including Evidence. Choose a different password and share it only with supervisors."));supervisor=field("Supervisor password","SetupSupervisorPassword");again=field("Confirm Supervisor password","SetupSupervisorConfirm");}
            else panel.Children.Add(Label("The copy keeps its current Supervisor password."));
            var feedback=Label("");feedback.Name="SetupFeedback";panel.Children.Add(feedback);var actions=Bar();panel.Children.Add(actions);
            foreach(var input in new[]{database,confirm,supervisor,again}.Where(box=>box!=null))input.PasswordChanged+=(s,e)=>feedback.Text="";
            var save=ActionButton("Continue",()=>{
                byte[] first=null,second=null;DatabaseCredentials access=null;
                try{
                    using(var password=database.SecurePassword)if(password.Length<12){feedback.Text="Use at least 12 characters for the database password.";return;}
                    if(supervisorRequired)using(var password=supervisor.SecurePassword)if(password.Length<12){feedback.Text="Use at least 12 characters for the Supervisor password.";return;}
                    first=ReadPassword(database,confirm);if(first==null){feedback.Text="The database passwords do not match.";return;}
                    if(supervisorRequired){second=ReadPassword(supervisor,again);if(second==null){feedback.Text="The Supervisor passwords do not match.";return;}if(first.SequenceEqual(second)){feedback.Text="Choose different database and Supervisor passwords.";return;}}
                    access=new DatabaseCredentials(first,second);accept(access);access=null;dialog.DialogResult=true;
                }catch(Exception error){feedback.Text=FailureDiagnostics.Brief(error);}
                finally{if(first!=null)Array.Clear(first,0,first.Length);if(second!=null)Array.Clear(second,0,second.Length);if(access!=null)access.Dispose();}
            });save.Name="SetupContinueButton";save.IsDefault=true;actions.Children.Add(save);
            var cancel=ActionButton("Cancel",dialog.Close);cancel.IsCancel=true;actions.Children.Add(cancel);
            dialog.Loaded+=(s,e)=>database.Focus();dialog.Closed+=(s,e)=>{database.Clear();confirm.Clear();if(supervisor!=null)supervisor.Clear();if(again!=null)again.Clear();};return dialog;
        }
        DatabaseCredentials SetupDatabasePasswords(bool supervisorRequired){DatabaseCredentials result=null;var dialog=CreateDatabaseSetupWindow(supervisorRequired,value=>result=value);try{if(dialog.ShowDialog()!=true){if(result!=null)result.Dispose();return null;}return result;}catch{if(result!=null)result.Dispose();throw;}}
        void SaveEncryptedCopy(){
            string output=SaveFile("Media Review database (*.sqlite)|*.sqlite",Path.GetFileNameWithoutExtension(engine.DatabasePath)+"-encrypted.sqlite");if(output==null)return;
            var access=SetupDatabasePasswords(!engine.VaultExists);if(access==null)return;
            try{Work(engine.RequestEncryptedCopy(output,access),"Encrypted database copy");}catch{access.Dispose();throw;}
        }
    }
}
