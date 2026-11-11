using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MediaReviewer {
    sealed partial class ReviewerWindow {
        sealed class DraftPreset {
            internal string Name,Description;internal int Threshold;internal bool Custom;
            public override string ToString(){return Name;}
        }
        void ShowDraftReview(){
            if(!Ready)return;
            bool available=DraftModelWorker.GemmaAvailable();
            if(available!=gemmaAvailable){gemmaAvailable=available;refresh=true;InvalidateThumbnails();}
            CreateDraftReviewWindow().ShowDialog();
        }
        Window GemmaPromptHelp(Window owner){
            var body=new StackPanel {Margin=new Thickness(18)};
            var dialog=Dialog("Custom rules help",body,530,380);dialog.Owner=owner;dialog.Name="GemmaPromptHelpWindow";
            body.Children.Add(Label("Use Find for what you want flagged and Ignore for exceptions. Describe visible, specific details."));
            body.Children.Add(Label("Find: Firearms\nIgnore: Toys and sports equipment"));
            body.Children.Add(Label("Find: Nudity or sexualized poses\nIgnore: Ordinary beach, pool and sports photos"));
            body.Children.Add(Label("Ignore rules take priority. Avoid vague words such as 'bad' or 'inappropriate'."));
            body.Children.Add(Label("Gemma can miss matches or suggest the wrong photo. Confirm its suggestions yourself."));
            var close=ActionButton("Close",dialog.Close);close.IsCancel=true;close.IsDefault=true;body.Children.Add(close);return dialog;
        }
        internal Window CreateDraftReviewWindow(){
            if(!Ready)return null;
            bool available=gemmaAvailable;
            var root=new DockPanel {Margin=new Thickness(18)};
            var dialog=Dialog("AI draft check",root,590,565);dialog.Name="AiDraftReviewWindow";
            var actions=Bar();Dock(root,actions,System.Windows.Controls.Dock.Bottom);
            var body=new StackPanel();root.Children.Add(new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            body.Children.Add(Label("Choose what to check. Review the matches in AI suggestions."));
            var model=new ComboBox {Name="DraftModel"};model.Items.Add("Marqo · fast image screening");
            if(available)model.Items.Add("Gemma 4 E2B · content review");
            model.SelectedIndex=available && useGemma ? 1 : 0;
            System.Windows.Automation.AutomationProperties.SetName(model,"AI review model");body.Children.Add(model);
            if(!available)body.Children.Add(Label(DraftModelWorker.GemmaCpuSupported() ? "Place both Gemma GGUF files beside FleetHacker to enable Gemma." : "Gemma is not supported by this device's processor."));

            var gemmaPanel=new StackPanel {Name="GemmaReviewOptions"};body.Children.Add(gemmaPanel);
            gemmaPanel.Children.Add(Label("Review preset"));
            var preset=new ComboBox {Name="GemmaReviewPreset",IsEnabled=!engine.DraftReviewActive};
            var more=new DraftPreset {Name="More suggestions",Threshold=55,Description="Flag nudity, sexualized posing and concerning unclear images."};
            var fewer=new DraftPreset {Name="Fewer suggestions",Threshold=90,Description="Focus on nudity and sexualized underwear posing."};
            var nudity=new DraftPreset {Name="Nudity only",Threshold=97,Description="Flag visible or clearly implied nudity, including artistic nudity."};
            var custom=new DraftPreset {Name="Custom",Threshold=97,Custom=true,Description="Flag matches to your rules. Ignore rules take priority."};
            foreach(var choice in new[]{more,fewer,nudity,custom})preset.Items.Add(choice);
            DraftPreset initial=gemmaCriteria!="" ? custom : gemmaThreshold>8 && gemmaThreshold<=55 ? more : gemmaThreshold>82 && gemmaThreshold<=90 ? fewer : gemmaThreshold>90 && gemmaThreshold<=97 ? nudity : null;
            if(initial==null){initial=new DraftPreset {Name="Saved filter",Threshold=gemmaThreshold,Description="Keep your saved filter, or choose a preset to change it."};preset.Items.Add(initial);}
            preset.SelectedItem=initial;System.Windows.Automation.AutomationProperties.SetName(preset,"Gemma review preset");gemmaPanel.Children.Add(preset);
            var description=Label("");description.Name="GemmaPresetDescription";gemmaPanel.Children.Add(description);
            var customPanel=new StackPanel {Name="GemmaCustomOptions"};gemmaPanel.Children.Add(customPanel);
            var rulesHeader=new DockPanel();customPanel.Children.Add(rulesHeader);
            var help=ActionButton("?",()=>GemmaPromptHelp(dialog).ShowDialog());help.Name="GemmaRulesHelp";help.Width=30;help.ToolTip="How to write find and ignore rules";System.Windows.Automation.AutomationProperties.SetName(help,"Help with Gemma custom rules");Dock(rulesHeader,help,System.Windows.Controls.Dock.Right);
            rulesHeader.Children.Add(Label("Custom rules"));
            string remembered=gemmaCriteria!="" ? gemmaCriteria : gemmaCustomCriteria;
            customPanel.Children.Add(Label("Find"));
            var rules=Editor(GemmaCriteria.Editable(remembered),GemmaCriteria.Limit);rules.Name="GemmaCustomRules";rules.AcceptsReturn=true;rules.TextWrapping=TextWrapping.Wrap;rules.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;rules.VerticalContentAlignment=VerticalAlignment.Top;rules.Height=65;rules.IsEnabled=!engine.DraftReviewActive;
            System.Windows.Automation.AutomationProperties.SetName(rules,"What Gemma should find");customPanel.Children.Add(rules);
            customPanel.Children.Add(Label("Ignore (optional)"));
            var ignore=Editor(GemmaCriteria.IgnoreText(remembered),GemmaCriteria.InstructionsLimit);ignore.Name="GemmaIgnoreRules";ignore.AcceptsReturn=true;ignore.TextWrapping=TextWrapping.Wrap;ignore.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;ignore.VerticalContentAlignment=VerticalAlignment.Top;ignore.Height=50;ignore.IsEnabled=!engine.DraftReviewActive;System.Windows.Automation.AutomationProperties.SetName(ignore,"What Gemma should ignore");customPanel.Children.Add(ignore);
            var feedback=Label("");feedback.Visibility=Visibility.Collapsed;customPanel.Children.Add(feedback);
            var devicePanel=new StackPanel();devicePanel.Children.Add(Label("Processing"));
            var device=new ComboBox {Name="GemmaProcessingDevice"};device.Items.Add("Auto (use GPU when available)");device.Items.Add("CPU only");device.SelectedIndex=gemmaCpuOnly ? 1 : 0;System.Windows.Automation.AutomationProperties.SetName(device,"Gemma processing device");devicePanel.Children.Add(device);
            gemmaPanel.Children.Add(new Expander {Header="Processing options",Content=devicePanel,Margin=new Thickness(0,5,0,5)});
            if(available){
                var database=engine;var loadStatus=Label(database.GemmaStatus);gemmaPanel.Children.Add(loadStatus);
                var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(250)};
                timer.Tick+=(s,e)=>{if(loadStatus.Text!=database.GemmaStatus)loadStatus.Text=database.GemmaStatus;};dialog.Loaded+=(s,e)=>timer.Start();dialog.Closed+=(s,e)=>timer.Stop();
            }
            var marqoPanel=new StackPanel {Name="MarqoReviewOptions"};body.Children.Add(marqoPanel);
            var threshold=Label("");marqoPanel.Children.Add(threshold);
            var slider=new Slider {Name="MarqoSuggestionThreshold",Minimum=1,Maximum=99,TickFrequency=1,IsSnapToTickEnabled=true,Value=marqoThreshold,Margin=new Thickness(0,0,0,6)};
            System.Windows.Automation.AutomationProperties.SetName(slider,"Marqo suggestion threshold in percent");marqoPanel.Children.Add(slider);
            marqoPanel.Children.Add(Label("Lower shows more possible matches. Higher narrows the list."));
            Action update=()=>{};
            var start=ActionButton("Save and run",()=>{});start.Name="AiDraftRun";start.ToolTip="Check new or changed photos and sampled video frames.";
            var saveSettings=ActionButton("Save settings",()=>{});saveSettings.Name="AiDraftSave";
            update=()=>{
                bool gemma=model.SelectedIndex==1;var choice=(DraftPreset)preset.SelectedItem;
                gemmaPanel.Visibility=gemma ? Visibility.Visible : Visibility.Collapsed;marqoPanel.Visibility=gemma ? Visibility.Collapsed : Visibility.Visible;
                customPanel.Visibility=choice.Custom ? Visibility.Visible : Visibility.Collapsed;description.Text=choice.Description;
                bool valid=!gemma || !choice.Custom || !String.IsNullOrWhiteSpace(rules.Text);
                saveSettings.IsEnabled=valid;start.IsEnabled=valid && task==null && !engine.Status.RunningScan && !engine.DraftReviewActive;
                threshold.Text="Suggestion threshold: "+((int)slider.Value).ToString(CultureInfo.InvariantCulture)+"%";
            };
            model.SelectionChanged+=(s,e)=>update();preset.SelectionChanged+=(s,e)=>{feedback.Visibility=Visibility.Collapsed;update();};slider.ValueChanged+=(s,e)=>update();rules.TextChanged+=(s,e)=>{feedback.Visibility=Visibility.Collapsed;update();};ignore.TextChanged+=(s,e)=>feedback.Visibility=Visibility.Collapsed;
            Action<bool> save=run=>Guard(()=>{
                bool gemma=model.SelectedIndex==1,cpuOnly=device.SelectedIndex==1;var choice=(DraftPreset)preset.SelectedItem;
                string criteria=null;int percent=(int)slider.Value;
                if(gemma){
                    percent=choice.Threshold;criteria="";
                    if(choice.Custom){
                        if(String.IsNullOrWhiteSpace(rules.Text))return;
                        try{criteria=GemmaCriteria.FromRules(rules.Text,ignore.Text);}catch(ArgumentException error){feedback.Text=error.Message;feedback.Visibility=Visibility.Visible;rules.Focus();return;}
                        // Retain existing advanced-rubric filters; new plain rules flag only clear matches.
                        percent=GemmaCriteria.IsInstructions(criteria) ? 97 : gemmaCustomThreshold;
                    }
                }
                Queue(engine.RequestDraftThreshold(percent,gemma,cpuOnly,gemma ? criteria : null),result=>{
                    if(!result.Success){Message("Could not save AI settings. "+result.Error);return;}
                    if(gemma){
                        if(criteria!=""){gemmaCustomCriteria=criteria;gemmaCustomThreshold=percent;}
                        else if(gemmaCriteria!=""){gemmaCustomCriteria=gemmaCriteria;gemmaCustomThreshold=gemmaThreshold;}
                        gemmaThreshold=percent;gemmaCpuOnly=cpuOnly;gemmaCriteria=criteria;
                    }else marqoThreshold=percent;
                    useGemma=gemma;
                    if(BucketKeys[bucket]==DraftReview.Bucket)NewPage(1);else{InvalidateThumbnails();refresh=true;}
                    Message(result.Value);if(run && Ready)Guard(()=>StartDraftReview(gemma,cpuOnly));
                });dialog.Close();
            });
            start.Click+=(s,e)=>save(true);saveSettings.Click+=(s,e)=>save(false);actions.Children.Add(start);actions.Children.Add(saveSettings);
            var cancel=ActionButton("Cancel",dialog.Close);cancel.IsCancel=true;actions.Children.Add(cancel);update();return dialog;
        }
        void StartDraftReview(bool gemma,bool cpuOnly){
            draftTask=engine.RequestDraftReview(gemma,cpuOnly);Message((gemma ? "Gemma" : "Marqo")+" draft check queued…");
            Queue(draftTask,result=>{draftTask=null;Message(result.Success ? result.Value : result.Error);});
        }
    }
}
