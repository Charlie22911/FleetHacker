using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MediaReviewer {
    sealed partial class ReviewerWindow:IDisposable {
        static readonly string[] BucketNames={"To be reviewed","Compliant","Non-compliant","Evidence"};
        static readonly string[] BucketKeys={"TO_REVIEW","COMPLIANT","NON_COMPLIANT","EVIDENCE"};
        readonly ObservableCollection<CardItem> cards=new ObservableCollection<CardItem>();
        readonly Dictionary<string,Action<UiResult>> pending=new Dictionary<string,Action<UiResult>>();
        readonly HashSet<Window> databaseWindows=new HashSet<Window>();
        readonly GallerySelection selection;
        readonly MediaLoader loader=new MediaLoader();
        readonly DispatcherTimer timer;
        readonly ViewGeneration view=new ViewGeneration();
        readonly ViewGeneration inspection=new ViewGeneration();
        readonly ReviewPreferences options=new ReviewPreferences();
        ReviewerEngine engine,closingDatabase;
        StatusSnapshot status;
        string pageRequest,task,preferencesSignature;
        ReviewPreferences savedPreferences;
        bool closing,allowClose,refresh,refillRequested,loadedPreferences,waitingOnClosePreference,switchingCatalog;
        DateTime? databaseClosedAt;
        int bucket,page=1,pageCount=1;
        long revision=-1;
        Window inspector;
        Action<MediaLoadResult> showInspection;

        internal Window Window {get;private set;}
        internal bool HasRenderedPreview {get{return cards.Any(c=>c.Image!=null) && Get<FrameworkElement>("PreviewPane").ActualWidth>0;}}
        T Get<T>(string name) where T:class {return (T)Window.FindName(name);}
        Button Button(string name){return Get<Button>(name);}
        ComboBox Combo(string name){return Get<ComboBox>(name);}
        TextBox Box(string name){return Get<TextBox>(name);}
        void Text(string name,string value){Get<TextBlock>(name).Text=value??"";}
        void Message(string value){Text("MessageText",value);}
        bool Ready {get{return engine!=null && engine.Ready && !engine.Faulted && !closing && !switchingCatalog;}}
        static string Id(){return Guid.NewGuid().ToString("N");}

        internal ReviewerWindow(Window window,bool persistWorkspace=true) {
            Window=window;
            var list=Get<ItemsControl>("CardList");list.ItemsSource=cards;
            list.AddHandler(ButtonBase.ClickEvent,new RoutedEventHandler(CardClick));
            selection=new GallerySelection(Get<ScrollViewer>("GalleryScroll"),list,cards);
            selection.Changed+=(s,e)=>UpdateControls();
            timer=new DispatcherTimer(DispatcherPriority.Background,window.Dispatcher){Interval=TimeSpan.FromMilliseconds(100)};
            timer.Tick+=(s,e)=>Tick();timer.Start();
            window.Closing+=Closing;
            Click("CatalogButton",ChooseDatabase);
            Click("BrowseButton",()=>{string selected=ChooseFolder("Choose a media source",Box("SourceBox").Text);if(selected!=null)Box("SourceBox").Text=selected;});
            Click("StartButton",StartScan);Click("ResumeButton",()=>engine.ResumeScan(status.ResumeId,Number("WorkerBox")));
            Click("CancelButton",()=>engine.CancelScan());
            Click("FormatsButton",ChooseFormats);Click("OptionsButton",ChooseOptions);
            foreach(FormatFamily family in Formats.Families)Combo("TypeBox").Items.Add(new ComboBoxItem {Content=family.Label,Tag=family.Key});
            Combo("ViewBox").SelectionChanged+=(s,e)=>{if(Combo("ViewBox").SelectedIndex>=0){bucket=Combo("ViewBox").SelectedIndex;NewPage(1);}};
            Click("FilterButton",()=>NewPage(1));Click("ResetButton",ResetView);
            Click("PreviousButton",()=>NewPage(page-1));Click("NextButton",()=>NewPage(page+1));Click("GoButton",GoToPage);
            Box("PageBox").KeyDown+=(s,e)=>{if(e.Key==Key.Enter){e.Handled=true;Guard(GoToPage);}};
            Box("SearchBox").KeyDown+=(s,e)=>{if(e.Key==Key.Enter){e.Handled=true;NewPage(1);}};
            Combo("PageSizeBox").SelectionChanged+=(s,e)=>NewPage(1);
            Combo("SortBox").SelectionChanged+=(s,e)=>{columnSort=null;NewPage(1);};
            Combo("TypeBox").SelectionChanged+=(s,e)=>NewPage(1);
            Combo("FitBox").SelectionChanged+=(s,e)=>SetFit();
            Click("SelectPageButton",()=>SelectAll(true));Click("ClearSelectionButton",()=>SelectAll(false));
            Click("CompliantButton",()=>ReviewWorkspaceItems("COMPLIANT"));Click("UnreviewedButton",()=>ReviewWorkspaceItems("TO_REVIEW"));
            Click("NonCompliantButton",()=>ReviewWorkspaceItems("NON_COMPLIANT"));Click("CaptureEvidenceButton",()=>ReviewWorkspaceItems("EVIDENCE"));
            Click("RetryPreviewsButton",()=>Work(engine.RequestPreviewRetry(Selected().Select(c=>c.Record.GroupId).ToArray()),"Preview retry"));
            Click("VaultButton",()=>{if(engine.VaultUnlocked){if(protectedWindow!=null)protectedWindow.Close();Work(engine.RequestVaultLock(),"Lock originals");}else Unlock(null);});
            Click("StoredOriginalButton",()=>Original(Selected().Single().Record));
            Click("ExportOriginalButton",()=>ExportOriginal(Selected().Single().Record));
            Click("RetainOriginalsButton",RetainOriginals);
            Click("MaintenanceButton",Maintenance);
            Click("PreviewTasksButton",PreviewTasks);
            Click("VerifyButton",()=>Verify(Selected().Single().Record));
            Click("IdentityHistoryButton",()=>Work(engine.RequestIdentityHistory(Selected().Single().Record.GroupId),"Identity history",r=>ShowText("Identity history",r.Value)));
            Click("LogButton",()=>Queue(engine.RequestLog(),r=>{if(r.Success)ShowText("Log",r.Value);else Message(r.Error);}));
            Click("CancelWorkButton",()=>{if(task!=null)engine.CancelOperation(task);});
            Click("RetryPreferencesButton",LoadPreferences);
            Click("ShutdownRetryButton",SaveBeforeClose);
            Click("ShutdownKeepSavedButton",()=>{waitingOnClosePreference=false;engine.CloseAsync();});
            Click("HelpButton",()=>ShowText("Help","Create or open a database on a local drive. Choose a local folder, mapped drive or UNC media source. The sidebar holds the review queues, view modes and catalog tools. Use the arrows at the panel edges to collapse or reopen them. Drag the thin preview divider immediately to the left of its arrow to resize it.\n\nDouble-click an item to open Inspect without changing the view. Choose Quick Review in the sidebar for directional review. Swipe or drag left for the previous item, right for the next item, up for Compliant, or down for Non-compliant. The arrow keys do the same when the image is focused. Use Details / notes or Inspect for a closer look.\n\nQuick scans common images and video. Deep also includes RAW and AppData. Use Resume scan after a cancellation.\n\nSaved previews and database metadata are not encrypted. The originals vault stores encrypted retained originals."));
            Click("AboutButton",About);
            SetUpWorkspace(persistWorkspace);
            SetUpCommandSections();
            UpdateControls();
        }

        void Click(string name,Action action){Button(name).Click+=(s,e)=>Guard(action);}
        void SetUpCommandSections() {
            Action summarize=()=>{
                Get<Expander>("ScanSettingsExpander").Header="Scan settings · "+((ComboBoxItem)Combo("ModeBox").SelectedItem).Content+" · "+Number("WorkerBox")+" workers"+(Box("AgeBox").Text.Trim()=="0" ? "" : " · age limit");
                var type=Combo("TypeBox").SelectedItem as ComboBoxItem;
                var sort=Combo("SortBox").SelectedItem as ComboBoxItem;
                Get<Expander>("FiltersExpander").Header="Filters and display · "+(type==null ? "All types" : type.Content)+" · "+(sort==null ? "First found" : sort.Content)+(String.IsNullOrWhiteSpace(Box("FolderBox").Text) ? "" : " · folder filter");
                Get<Expander>("ReviewNotesExpander").Header="Reviewer and notes · "+(String.IsNullOrWhiteSpace(Box("ReviewerBox").Text) ? "No reviewer label" : Box("ReviewerBox").Text)+(String.IsNullOrWhiteSpace(Box("NotesBox").Text) ? "" : " · notes entered");
            };
            foreach(string name in new[]{"ModeBox","WorkerBox","TypeBox","SortBox"})Combo(name).SelectionChanged+=(s,e)=>summarize();
            foreach(string name in new[]{"AgeBox","FolderBox","ReviewerBox","NotesBox"})Box(name).TextChanged+=(s,e)=>summarize();
            Box("FolderBox").KeyDown+=(s,e)=>{if(e.Key==Key.Enter){e.Handled=true;NewPage(1);}};
            // Keep a useful gallery area even with several sections open on a short screen.
            Window.SizeChanged+=(s,e)=>Get<FrameworkElement>("TopControls").MaxHeight=Math.Max(220,Math.Min(400,Window.ActualHeight*0.44));
            summarize();
        }
        void Guard(Action action){try{action();}catch(Exception e){Message(e.GetBaseException().Message);}}
        void Queue(string id,Action<UiResult> action){pending.Add(id,action);}
        void Work(string id,string label,Action<UiResult> complete=null,bool notifyOnFailure=false) {
            task=id;Message(label+" queued…");
            Queue(id,r=>{if(task==id)task=null;Message(r.Success ? (r.Value??(r.Reconnection==null ? label+" complete." : r.Reconnection.Summary)) : label+" failed: "+r.Error);if(complete!=null && (r.Success || notifyOnFailure))complete(r);});
        }

        void ChooseDatabase() {
            var picker=new OpenFileDialog {
                Title="Select an existing catalog or enter a new filename",
                Filter="Catalogs and Evidence packages (*.sqlite;*.db;*.mcrevidence)|*.sqlite;*.db;*.mcrevidence|All files (*.*)|*.*",
                CheckFileExists=false, CheckPathExists=true, DefaultExt=".sqlite"
            };
            if(picker.ShowDialog(Window)!=true)return;
            string path=picker.FileName;
            if(String.Equals(Path.GetExtension(path),".mcrevidence",StringComparison.OrdinalIgnoreCase))OpenPackage(path);
            else Open(path,IsNewCatalogPath(path));
        }

        internal static bool IsNewCatalogPath(string path) {
            try {
                if((File.GetAttributes(Paths.Io(path)) & FileAttributes.Directory)!=0)
                    throw new ArgumentException("Select a catalog file or enter a new filename.");
                return false;
            } catch(FileNotFoundException) { return true; }
        }

        internal void Open(string path,bool create) {
            if(closing || closingDatabase!=null || engine!=null && ((!Ready && !engine.Faulted) || status!=null && status.RunningScan || pending.Count!=0 || task!=null))throw new InvalidOperationException("Wait for current work to finish before changing databases.");
            if(engine==null){StartOpen(path,create,null);return;}
            var old=engine;string save=SavePreferences(old);
            if(save!=null){
                string signature=options.Signature;
                switchingCatalog=true;Message("Saving preferences before changing catalogs…");UpdateControls();
                Queue(save,r=>{
                    switchingCatalog=false;
                    if(r.Success)preferencesSignature=signature;
                    if(closing){if(r.Success)SaveBeforeClose();else PreferenceCloseFailed(r.Error);return;}
                    if(r.Success)SwitchCatalog(old,path,create);
                    else Message("Could not save preferences. The current catalog is still open. "+r.Error);
                });
                return;
            }
            SwitchCatalog(old,path,create);
        }

        void SwitchCatalog(ReviewerEngine old,string path,bool create) {
            if(inspector!=null)inspector.Close();
            if(protectedWindow!=null)protectedWindow.Close();
            CloseDatabaseWindows();
            closingDatabase=old;engine=null;UpdateControls();Text("ActivityText","Closing the current database…");
            old.CloseAsync();
            var wait=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
            wait.Tick+=(s,e)=>{if(old.Closed){wait.Stop();closingDatabase=null;if(!closing)Guard(()=>StartOpen(path,create,null));}};
            wait.Start();
        }

        void StartOpen(string path,bool create,string html) {
            if(inspector!=null)inspector.Close();
            if(protectedWindow!=null)protectedWindow.Close();
            ClearCards();pending.Clear();view.Advance();page=1;quickIndex=0;bucket=0;revision=-1;status=null;pageRequest=null;refillRequested=false;
            Combo("ViewBox").SelectedIndex=0;
            refresh=true;loadedPreferences=false;preferencesSignature=null;savedPreferences=null;offeredImport=false;
            options.DefaultSourceRoot="";Box("SourceBox").Clear();
            engine=html==null ? ReviewerEngine.Open(path,create) : ReviewerEngine.ImportHtml(html,path);
            Text("DatabaseLabel",path);Text("ActivityText",html!=null ? "Importing HTML database…" : "Opening database…");
            Text("PreferencesText","Loading saved preferences…");UpdateControls();
        }

        void LoadPreferences() {
            Button("RetryPreferencesButton").Visibility=Visibility.Hidden;
            Queue(engine.RequestPreferences(),r=>{
                if(!r.Success){Text("PreferencesText","Preferences unavailable: "+r.Error);Button("RetryPreferencesButton").Visibility=Visibility.Visible;return;}
                savedPreferences=r.Preferences;preferencesSignature=r.Preferences.Signature;
                Combo("PageSizeBox").SelectedItem=r.Preferences.PageSize;
                Combo("WorkerBox").SelectedItem=r.Preferences.WorkerCount;
                Combo("FitBox").SelectedIndex=r.Preferences.Fit=="UniformToFill" ? 1 : 0;
                Combo("ModeBox").SelectedIndex=Array.IndexOf(new[]{"Quick","Deep","Custom"},r.Preferences.Mode);
                Box("ReviewerBox").Text=String.IsNullOrEmpty(r.Preferences.Reviewer) ? WindowsReviewerLabel() : r.Preferences.Reviewer;Box("AgeBox").Text=r.Preferences.ModifiedAgeYears.ToString(CultureInfo.InvariantCulture);
                options.Families=r.Preferences.Families;options.IncludeApplicationData=r.Preferences.IncludeApplicationData;
                options.IncludeRootFiles=r.Preferences.IncludeRootFiles;options.ScanArchives=r.Preferences.ScanArchives;
                options.SniffRenamed=r.Preferences.SniffRenamed;options.FullHashScans=r.Preferences.FullHashScans;
                options.DefaultSourceRoot=r.Preferences.DefaultSourceRoot;
                if(String.IsNullOrWhiteSpace(Box("SourceBox").Text))Box("SourceBox").Text=options.DefaultSourceRoot;
                Text("PreferencesText",String.IsNullOrEmpty(r.Preferences.Warning) ? "Preferences loaded for this database." : r.Preferences.Warning);
            });
        }

        string SavePreferences(ReviewerEngine target) {
            if(savedPreferences==null || savedPreferences.ReadOnly || !target.Ready || target.Faulted)return null;
            options.PageSize=Number("PageSizeBox");options.WorkerCount=Number("WorkerBox");options.ModifiedAgeYears=Age();
            options.Fit=Tag("FitBox");options.Mode=Mode();options.Reviewer=Box("ReviewerBox").Text;
            return options.Signature==preferencesSignature ? null : target.RequestSavePreferences(options,Id());
        }

        int Number(string name){return (int)Combo(name).SelectedItem;}
        string Tag(string name){return (string)((ComboBoxItem)Combo(name).SelectedItem).Tag;}
        string Mode(){return (string)((ComboBoxItem)Combo("ModeBox").SelectedItem).Content;}
        int Age(){int age;if(!Int32.TryParse(Box("AgeBox").Text,out age) || age<0 || age>ScanOptions.MaxModifiedAgeYears)throw new ArgumentException("Enter an age from 0 to "+ScanOptions.MaxModifiedAgeYears+" years.");return age;}
        void StartScan() {
            string mode=Mode();
            var request=new ScanOptions {RootPath=Box("SourceBox").Text.Trim(),WorkerCount=Number("WorkerBox"),ModifiedAgeYears=Age(),
                ExcludeApplicationData=mode!="Deep" && !options.IncludeApplicationData,IncludeRootFiles=options.IncludeRootFiles,ScanArchives=options.ScanArchives,SniffRenamed=options.SniffRenamed,
                FingerprintPolicy=options.FullHashScans ? FingerprintPolicy.Full() : new FingerprintPolicy(),
                Extensions=mode=="Quick" ? Formats.Quick : mode=="Deep" ? Formats.Deep : options.Families.SelectMany(Formats.Filter).ToArray()};
            engine.StartScan(request);Message("Scan queued…");
        }

        void SelectAll(bool value){foreach(CardItem card in cards)if(card.CanSelect)card.IsSelected=value;UpdateControls();}
        CardItem[] Selected(){if(display==ReviewView.Quick){var card=QuickCard();return card!=null && card.CanSelect ? new[]{card} : new CardItem[0];}return cards.Where(c=>c.IsSelected && c.CanSelect).ToArray();}
        void SetFit(){var style=new Style(typeof(Image));style.Setters.Add(new Setter(Image.StretchProperty,Tag("FitBox")=="Uniform" ? Stretch.Uniform : Stretch.UniformToFill));style.Setters.Add(new Setter(Image.HorizontalAlignmentProperty,HorizontalAlignment.Center));style.Setters.Add(new Setter(Image.VerticalAlignmentProperty,VerticalAlignment.Center));Window.Resources["ThumbnailStyle"]=style;}
        void ResetView(){Box("SearchBox").Clear();Box("FolderBox").Clear();columnSort=null;Combo("TypeBox").SelectedIndex=0;Combo("SortBox").SelectedIndex=0;Combo("FitBox").SelectedIndex=0;NewPage(1);}
        void GoToPage(){int requested;if(!Int32.TryParse(Box("PageBox").Text,out requested) || requested<1 || requested>pageCount)throw new ArgumentException("Enter a page from 1 to "+pageCount+".");NewPage(requested);}
        void NewPage(int target){if(!Ready)return;page=Math.Max(1,target);quickIndex=0;view.Advance();refresh=true;refillRequested=false;ClearCards();Get<ScrollViewer>("GalleryScroll").ScrollToTop();}

        void ReadPage(bool refill) {
            var request=new PageRequest {Generation=view.Current,Bucket=BucketKeys[bucket],Page=page,PageSize=Number("PageSizeBox"),
                Search=Box("SearchBox").Text,Folder=Box("FolderBox").Text,Type=Tag("TypeBox"),Sort=columnSort??Tag("SortBox"),SortDescending=columnSort==null ? (bool?)null : columnDescending,
                ExistingIds=refill ? cards.Select(c=>c.Record.GroupId).ToArray() : new long[0]};
            pageRequest=engine.RequestPage(request);refresh=false;refillRequested=false;
            Queue(pageRequest,r=>{pageRequest=null;if(!view.Accept(r.Generation))return;if(r.Success)ApplyPage(r.Page);else Message(r.Error);});
        }

        void ApplyPage(PageData result) {
            revision=result.Revision;page=result.Page;
            pageCount=(int)Math.Max(1,Math.Min(Int32.MaxValue,(result.Total+result.PageSize-1)/result.PageSize));
            Box("PageBox").Text=page.ToString(CultureInfo.InvariantCulture);
            Text("PageLabel","Page "+page+" of "+pageCount+" · "+result.Total.ToString("N0")+" matching items");
            for(int i=0;i<BucketNames.Length;i++)Button("Queue"+i).Content=((ComboBoxItem)Combo("ViewBox").Items[i]).Content=BucketNames[i]+" ("+result.Totals[i].ToString("N0")+")";
            if(!result.Refill) {
                var ids=new HashSet<long>(result.Rows.Select(row=>row.GroupId));
                for(int i=cards.Count-1;i>=0;i--)if(!ids.Contains(cards[i].Record.GroupId))Remove(cards[i]);
            }
            for(int i=0;i<result.Rows.Count;i++){
                MediaRow row=result.Rows[i];CardItem card=cards.FirstOrDefault(c=>c.Record.GroupId==row.GroupId);
                if(card==null){card=new CardItem(row);card.PropertyChanged+=CardChanged;cards.Insert(result.Refill ? cards.Count : i,card);}
                else {card.Update(row);if(!result.Refill && cards.IndexOf(card)!=i)cards.Move(cards.IndexOf(card),i);}
            }
            Get<Border>("EmptyPanel").Visibility=cards.Count==0 ? Visibility.Visible : Visibility.Collapsed;
            Text("EmptyTitle","No matching items");Text("EmptyText","Start a scan or change the current bucket and filters.");
            if(cards.Count!=0)quickIndex=Math.Min(quickIndex,cards.Count-1);
        }

        void ClearCards(){CancelQuickGesture();ResetQuickPreview();foreach(var card in cards)card.PropertyChanged-=CardChanged;cards.Clear();}
        void Remove(CardItem card){
            int index=cards.IndexOf(card);
            if(index<0)return;
            if(display==ReviewView.Quick){
                int current=QuickPosition;
                if(index==current){CancelQuickGesture();ResetQuickPreview();}
                if(index<current)quickIndex=current-1;
            }
            card.PropertyChanged-=CardChanged;cards.RemoveAt(index);
        }
        void CardChanged(object sender,PropertyChangedEventArgs e){if((e.PropertyName=="IsSelected" || e.PropertyName=="CanSelect") && !selection.Updating)UpdateControls();}
        void CardClick(object sender,RoutedEventArgs e) {
            var button=e.Source as Button;var card=button==null ? null : button.DataContext as CardItem;
            if(card==null || !Ready)return;
            if(button.Name=="CardDetailsButton")Guard(()=>Details(card.Record));
            if(button.Name=="CardInspectButton")Guard(()=>Inspect(card.Record));
        }

        void Review(string destination,CardItem[] chosen,Action<UiResult> done=null,string reviewer=null,string notes=null,Func<bool> stillIntended=null) {
            if(chosen.Length==0 || stillIntended!=null && !stillIntended())return;
            bool protectedChange=destination=="NON_COMPLIANT" || destination=="EVIDENCE" || chosen.Any(c=>c.Record.Status=="EVIDENCE");
            if(protectedChange && !engine.VaultUnlocked){
                long generationAtPrompt=view.Current;
                var originalRows=chosen.Select(c=>c.Record).ToArray();
                string label=reviewer??Box("ReviewerBox").Text, draft=notes??Box("NotesBox").Text;
                foreach(var card in chosen)card.IsPending=true;
                Action release=()=>{foreach(var card in chosen)card.IsPending=false;};
                Unlock(()=>{
                    release();
                    if(!view.Accept(generationAtPrompt) || stillIntended!=null && !stillIntended() || chosen.Where((card,i)=>!card.CanSelect || card.Record.ContentId!=originalRows[i].ContentId || card.Record.ReviewRevision!=originalRows[i].ReviewRevision).Any()){
                        Message("The selection or saved review changed while unlocking. Select the item again.");return;
                    }
                    Review(destination,chosen,done,label,draft,stillIntended);
                },release);return;
            }
            var request=new ReviewRequest {Status=destination,OperationId=Id(),Reviewer=reviewer??Box("ReviewerBox").Text,Notes=notes??Box("NotesBox").Text,
                GroupIds=chosen.Select(c=>c.Record.GroupId).ToArray(),ExpectedRevisions=chosen.ToDictionary(c=>c.Record.GroupId,c=>c.Record.ReviewRevision)};
            bool fromEvidence=chosen.Any(c=>c.Record.Status=="EVIDENCE");
            if(destination=="EVIDENCE" && fromEvidence)throw new InvalidOperationException("The selected item is already in Evidence.");
            if(fromEvidence && destination!="EVIDENCE" && MessageBox.Show(Window,"Move the selected items out of Evidence?","Move Evidence",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
            string id=fromEvidence ? engine.RequestEvidenceReclassify(request) : destination=="EVIDENCE" ? engine.RequestOriginalRetention(request) : engine.RequestReview(request);
            if(protectedChange)task=id;
            foreach(CardItem card in chosen)card.IsPending=true;
            long generation=view.Current;
            Queue(id,r=>{
                if(task==id)task=null;
                foreach(CardItem card in chosen){card.IsPending=false;if(r.Success){card.IsSelected=false;if(view.Accept(generation) && destination!=BucketKeys[bucket])Remove(card);}}
                Message(r.Success ? "Review saved." : "Review failed. Selection retained. "+r.Error);
                if(r.Success){refresh=true;refillRequested=view.Accept(generation) && destination!=BucketKeys[bucket];}
                if(done!=null)done(r);
            });
            Message("Review pending…");
        }

        void Tick() {
            if(closing){ShutdownTick();return;}
            if(engine==null)return;
            status=engine.Status;
            Text("ActivityText",status.RunningScan ? String.Format("{0} · user {1}/{2} · {3:N0}/{4:N0} files processed · {5:N0} errors · {6:N0} queued · WAL {7:N1} MiB",
                status.Stage,status.CompletedUsers,status.TotalUsers,status.ProcessedFiles,status.DiscoveredFiles,status.FailedFiles,status.QueueCount,status.WalBytes/1048576.0) : status.Stage);
            if(engine.Faulted && engine.DetectedHtml && pending.Count==0){OfferImport();return;}
            if(engine.Faulted && !String.IsNullOrEmpty(status.Error))Message(status.Error);
            UiResult result;
            for(int i=0;i<8 && engine.TryTakeResult(out result);i++)DispatchResult(result);
            if(Ready && !loadedPreferences){loadedPreferences=true;Guard(LoadPreferences);}
            if(Ready && pageRequest==null && (refresh || status.Revision!=revision) && !selection.Dragging)Guard(()=>ReadPage(refillRequested && cards.Count>0));
            for(int i=0;i<12;i++) {
                MediaLoadResult media;if(!loader.TryTakeResult(out media))break;
                if(media.Kind=="Inspect"){
                    if(inspection.Accept(media.Generation)){if(showInspection!=null)showInspection(media);else ApplyQuickPreview(media);}
                    continue;
                }
                if(!view.Accept(media.Generation))continue;
                CardItem card=cards.FirstOrDefault(c=>c.Record.GroupId==media.GroupId && c.Record.ContentId==media.ContentId && c.Record.PreviewRevision==media.PreviewRevision);
                if(card!=null){card.SetImage(media.Image,media.Error);if(media.Image!=null)card.Record.PreviewBytes=null;}
            }
            var focus=FocusedCard();
            IEnumerable<CardItem> toDecode=display==ReviewView.Thumbnails ? (IEnumerable<CardItem>)cards : focus==null ? new CardItem[0] : new[]{focus};
            foreach(CardItem card in toDecode)if(card.CanLoadImage){if(!loader.QueueThumbnail(card.Record.GroupId,card.Record.ContentId,view.Current,card.Record.PreviewBytes,card.Record.PreviewRevision))break;card.ImageQueued=true;}
            if(Ready)Guard(LoadQuickPreview);
            var progress=task==null ? null : engine.GetOperationProgress(task);
            if(progress!=null)Text("ActivityText",progress.Stage+" · "+progress.Completed.ToString("N0")+(progress.Total.HasValue ? " / "+progress.Total.Value.ToString("N0") : "")+" "+progress.Unit);
            UpdateControls();
        }

        void DispatchResult(UiResult result) {
            Action<UiResult> action;
            try{if(pending.TryGetValue(result.RequestId,out action)){pending.Remove(result.RequestId);action(result);}}
            catch(Exception e){Message(e.GetBaseException().Message);}
            finally{if(result.Evidence!=null)result.Evidence.Dispose();}
        }

        void UpdateControls() {
            bool ready=Ready,scanning=status!=null && status.RunningScan;
            int count=Selected().Length;
            Text("SelectionLabel",count+" selected");
            Button("CatalogButton").IsEnabled=!closing && !switchingCatalog && closingDatabase==null && !scanning && task==null && pending.Count==0 && (engine==null || engine.Ready || engine.Faulted);
            Button("StartButton").IsEnabled=ready && !scanning;
            Button("ResumeButton").IsEnabled=ready && !scanning && status!=null && !String.IsNullOrEmpty(status.ResumeId);
            Button("CancelButton").IsEnabled=ready && scanning;
            foreach(string name in new[]{"FilterButton","ResetButton","GoButton","LogButton","MaintenanceButton","PreviewTasksButton","VaultButton"})Button(name).IsEnabled=ready;
            Button("PreviousButton").IsEnabled=ready && page>1;Button("NextButton").IsEnabled=ready && page<pageCount;
            Combo("ViewBox").IsEnabled=ready;
            foreach(string name in new[]{"CompliantButton","NonCompliantButton","UnreviewedButton","RetryPreviewsButton","RetainOriginalsButton","CaptureEvidenceButton","ClearSelectionButton"})Button(name).IsEnabled=ready && count>0;
            foreach(string name in new[]{"VerifyButton","IdentityHistoryButton","StoredOriginalButton","ExportOriginalButton"})Button(name).IsEnabled=ready && count==1;
            Button("SelectPageButton").IsEnabled=ready && display!=ReviewView.Quick && cards.Any(c=>c.CanSelect);
            Button("ClearSelectionButton").IsEnabled=ready && display!=ReviewView.Quick && count>0;
            Button("CancelWorkButton").Visibility=ready && task!=null ? Visibility.Visible : Visibility.Collapsed;
            Button("VaultButton").Content=ready && engine.VaultUnlocked ? "Lock originals" : "Unlock originals";
            UpdateWorkspace();
        }

        void Closing(object sender,CancelEventArgs args) {
            if(allowClose)return;args.Cancel=true;if(closing)return;closing=true;
            SaveWorkspace();UpdateControls();
            if(inspector!=null)inspector.Close();
            if(protectedWindow!=null)protectedWindow.Close();
            CloseDatabaseWindows();
            Get<Border>("ShutdownPanel").Visibility=Visibility.Visible;
            loader.CloseAsync();
            if(switchingCatalog){waitingOnClosePreference=true;Text("ShutdownStage","Waiting for the pending preference save…");}
            else SaveBeforeClose();
        }

        void SaveBeforeClose() {
            Button("ShutdownRetryButton").Visibility=Button("ShutdownKeepSavedButton").Visibility=Visibility.Collapsed;
            Text("ShutdownStage","Saving preferences and closing the database…");
            waitingOnClosePreference=false;
            if(engine==null)return;
            try {
                string save=SavePreferences(engine);
                if(save==null){engine.CloseAsync();return;}
                waitingOnClosePreference=true;
                Queue(save,r=>{if(r.Success){waitingOnClosePreference=false;engine.CloseAsync();}else PreferenceCloseFailed(r.Error);});
            }catch(Exception error){PreferenceCloseFailed(error.GetBaseException().Message);}
        }

        void PreferenceCloseFailed(string error) {
            waitingOnClosePreference=true;
            Text("ShutdownStage","Preferences could not be saved. "+error);
            Button("ShutdownRetryButton").Visibility=Button("ShutdownKeepSavedButton").Visibility=Visibility.Visible;
        }

        void ShutdownTick() {
            UiResult result;
            if(engine!=null)while(engine.TryTakeResult(out result))DispatchResult(result);
            if(closingDatabase!=null || waitingOnClosePreference)return;
            if(engine!=null && !engine.Closed){if(!String.IsNullOrEmpty(engine.CloseStage))Text("ShutdownStage",engine.CloseStage);return;}
            if(!loader.Closed){Text("ShutdownStage","Database closed. Stopping preview workers…");return;}
            if(engine!=null){
                if(!databaseClosedAt.HasValue){databaseClosedAt=DateTime.UtcNow;Text("ShutdownStage","Database closed. Committed work is saved.");Get<ProgressBar>("ShutdownProgress").IsIndeterminate=false;Get<ProgressBar>("ShutdownProgress").Value=100;return;}
                if((DateTime.UtcNow-databaseClosedAt.Value).TotalMilliseconds<550)return;
            }
            timer.Stop();allowClose=true;Window.Close();
        }

        public void Dispose(){timer.Stop();selection.Dispose();if(!loader.Closed)loader.Dispose();if(engine!=null && !engine.Closed)engine.Dispose();}
    }
}
