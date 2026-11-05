using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MediaReviewer {
    internal sealed class WorkspacePreferences {
        public WorkspacePreferences(){}
        public bool Dark {get;set;}
        public bool Thumbnails {get;set;}
        public bool SidebarCollapsed {get;set;}
        public bool PreviewCollapsed {get;set;}
        public double PreviewWidth {get;set;}
        public bool TopCollapsed {get;set;}
        public double TileSize {get;set;}
        public double[] Bounds {get;set;}

        static string FilePath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Media Compliance Reviewer","workspace.json");}}
        static JavaScriptSerializer Serializer(){return new JavaScriptSerializer {MaxJsonLength=4096,RecursionLimit=8};}
        internal static WorkspacePreferences Load() {
            try {
                using(var stream=new FileStream(FilePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                using(var input=new BinaryReader(stream)) {
                    byte[] bytes=input.ReadBytes(4097);
                    if(bytes.Length<=4096)return Serializer().Deserialize<WorkspacePreferences>(Encoding.UTF8.GetString(bytes))??new WorkspacePreferences();
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(SecurityException){}catch(ArgumentException){}catch(InvalidOperationException){}
            return new WorkspacePreferences();
        }

        internal void Save() {
            string path=FilePath,partial=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            bool owned=false;
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var stream=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    owned=true;
                    using(var output=new StreamWriter(stream,new UTF8Encoding(false)))output.Write(Serializer().Serialize(this));
                }
                if(File.Exists(path))File.Replace(partial,path,null);
                else File.Move(partial,path);
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(SecurityException){}
            finally{if(owned)try{File.Delete(partial);}catch(IOException){}catch(UnauthorizedAccessException){}catch(SecurityException){}}
        }
    }

    sealed partial class ReviewerWindow {
        const double PreviewMinWidth=190,PreviewDefaultWidth=218,PreviewMaxWidth=720;
        enum ReviewView { List, Thumbnails, Quick }
        ReviewView display;
        WorkspacePreferences workspace;
        bool persistWorkspace;
        int quickIndex;
        string columnSort;
        bool columnDescending;
        MediaRow quickSource;
        int quickDecodeSide;
        static string WindowsReviewerLabel(){
            string name=Environment.UserDomainName+"\\"+Environment.UserName;
            return name.Length<=128 ? name : Environment.UserName.Length<=128 ? Environment.UserName : "";
        }

        void SetUpWorkspace(bool persist) {
            persistWorkspace=persist;
            workspace=persist ? WorkspacePreferences.Load() : new WorkspacePreferences();
            ApplyTheme();
            var list=Get<ListView>("ReviewList");list.ItemsSource=cards;
            list.AddHandler(GridViewColumnHeader.ClickEvent,new RoutedEventHandler(SortColumn));
            list.MouseDoubleClick+=(s,e)=>{
                var element=e.OriginalSource as DependencyObject;
                var item=element==null ? null : ItemsControl.ContainerFromElement(list,element) as ListViewItem;
                var card=item==null ? null : item.Content as CardItem;
                if(Ready && card!=null){e.Handled=true;OpenQuick(card);}
            };
            var gallery=Get<ItemsControl>("CardList");
            gallery.PreviewMouseLeftButtonDown+=(s,e)=>{
                if(e.ClickCount!=2 || !Ready)return;
                var element=e.OriginalSource as DependencyObject;
                for(var control=element as FrameworkElement;control!=null && control!=gallery;control=VisualTreeHelper.GetParent(control) as FrameworkElement)
                    if(control is ButtonBase)return;
                var container=element==null ? null : ItemsControl.ContainerFromElement(gallery,element) as FrameworkElement;
                var card=container==null ? null : container.DataContext as CardItem;
                if(card!=null){e.Handled=true;OpenQuick(card);}
            };
            for(int i=0;i<BucketKeys.Length;i++){
                int index=i;Click("Queue"+i,()=>Combo("ViewBox").SelectedIndex=index);
            }
            Click("ThemeButton",()=>{workspace.Dark=!workspace.Dark;ApplyTheme();});
            Click("ListModeButton",()=>SetView(ReviewView.List));
            Click("ThumbnailsModeButton",()=>SetView(ReviewView.Thumbnails));
            Click("QuickModeButton",()=>OpenQuick(FocusedCard()));
            Click("RefreshViewButton",()=>{view.Advance();refresh=true;refillRequested=false;foreach(var card in cards)card.ImageQueued=false;});
            Click("QueueSidebarButton",()=>{workspace.SidebarCollapsed=!workspace.SidebarCollapsed;UpdateWorkspace();});
            Click("TopControlsButton",()=>{workspace.TopCollapsed=!workspace.TopCollapsed;UpdateWorkspace();});
            Click("PreviewPaneButton",()=>{workspace.PreviewCollapsed=!workspace.PreviewCollapsed;UpdateWorkspace();});
            Click("PaneDetailsButton",()=>{var card=FocusedCard();if(card!=null)Details(card.Record);});
            Click("PaneInspectButton",()=>{var card=FocusedCard();if(card!=null)Inspect(card.Record);});
            Click("PaneOpenButton",()=>{var card=FocusedCard();if(card!=null)Work(engine.RequestOpenOriginal(card.Record),"Open original");});
            Click("PaneCaseButton",()=>{var card=FocusedCard();if(card!=null)Work(engine.RequestCaseSheet(card.Record),"Case sheet",r=>ShowText("Non-compliant case sheet",r.Value));});
            Click("QuickPreviousButton",()=>{MoveQuick(-1);FocusQuick();});Click("QuickNextButton",()=>{MoveQuick(1);FocusQuick();});
            Click("QuickNonCompliantButton",()=>{FocusQuick();ReviewWorkspaceItems("NON_COMPLIANT");});
            Click("QuickCompliantButton",()=>{FocusQuick();ReviewWorkspaceItems("COMPLIANT");});
            Click("QuickInspectButton",()=>{var card=QuickCard();if(card!=null)Inspect(card.Record);});
            SetUpQuickGestures();
            Window.PreviewKeyDown+=ReviewKey;
            Get<Grid>("ReviewArea").SizeChanged+=(s,e)=>{
                if(e.WidthChanged && Get<GridSplitter>("PreviewDivider").Visibility==Visibility.Visible)UpdatePreviewWidthLimit();
            };
            Window.SourceInitialized+=(s,e)=>RestoreBounds();
            Get<Slider>("ThumbnailSize").Value=workspace.TileSize>=160 && workspace.TileSize<=320 ? workspace.TileSize : 220;
            SetView(workspace.Thumbnails ? ReviewView.Thumbnails : ReviewView.List);
        }

        void ApplyTheme() {
            string[] keys={"PageBrush","SurfaceBrush","TextBrush","MutedBrush","InsetBrush","BorderBrush","SidebarBrush","SidebarTextBrush","SidebarActiveBrush","FocusBrush","PrimaryBrush","PrimaryTextBrush","GoodBrush","GoodTextBrush","BadBrush","BadTextBrush"};
            string[] light={"#F4F7FB","#FFFFFF","#1C2B3A","#506176","#E8EEF5","#D5DEE8","#10243F","#F3F8FF","#24476B","#2873AC","#147A64","#FFFFFF","#E7F6EF","#0B6540","#FDECEC","#9D221A"};
            string[] dark={"#141D28","#202C3B","#F0F5FA","#C3D2E3","#182331","#53647A","#10243F","#F3F8FF","#325980","#8AD1FF","#137960","#FFFFFF","#194335","#C6F4DC","#592D37","#FFE0E0"};
            Color[] contrast={SystemColors.WindowColor,SystemColors.WindowColor,SystemColors.WindowTextColor,SystemColors.WindowTextColor,SystemColors.WindowColor,SystemColors.WindowTextColor,SystemColors.WindowColor,SystemColors.WindowTextColor,SystemColors.WindowColor,SystemColors.HighlightColor,SystemColors.WindowColor,SystemColors.WindowTextColor,SystemColors.WindowColor,SystemColors.WindowTextColor,SystemColors.WindowColor,SystemColors.WindowTextColor};
            for(int i=0;i<keys.Length;i++){
                var brush=new SolidColorBrush(SystemParameters.HighContrast ? contrast[i] : (Color)ColorConverter.ConvertFromString((workspace.Dark ? dark : light)[i]));
                brush.Freeze();Window.Resources[keys[i]]=brush;
            }
            Button("ThemeButton").Content=workspace.Dark ? "Light mode" : "Dark mode";
            Button("ThemeButton").IsEnabled=!SystemParameters.HighContrast;
        }

        void SetView(ReviewView mode) {
            if(display==ReviewView.Quick && mode!=display){CancelQuickGesture();ResetQuickPreview();}
            display=mode;
            if(mode!=ReviewView.Quick)workspace.Thumbnails=mode==ReviewView.Thumbnails;
            UpdateControls();
        }
        void OpenQuick(CardItem card){
            if(!Ready || card==null)return;
            int index=cards.IndexOf(card);if(index<0)return;
            CancelQuickGesture();ResetQuickPreview();quickIndex=index;SetView(ReviewView.Quick);FocusQuick();
        }
        void FocusQuick(){if(display==ReviewView.Quick)Get<FrameworkElement>("QuickImageHost").Focus();}

        CardItem QuickCard(){return cards.Count==0 ? null : cards[Math.Min(Math.Max(0,quickIndex),cards.Count-1)];}
        CardItem FocusedCard(){return display==ReviewView.Quick ? QuickCard() : cards.FirstOrDefault(c=>c.IsSelected)??cards.FirstOrDefault();}
        void ResetQuickPreview(){
            if(quickSource!=null && inspector==null)loader.CancelInspect(inspection.Advance());
            quickSource=null;quickDecodeSide=0;Get<Image>("QuickDetailImage").Source=null;
        }
        void LoadQuickPreview() {
            if(display!=ReviewView.Quick || inspector!=null)return;
            var card=QuickCard();var row=card==null ? null : card.Record;
            if(row==null || row.Status=="EVIDENCE" || row.SourceIsHistorical || String.IsNullOrEmpty(row.RootPath) || String.IsNullOrEmpty(row.RelativePath)){ResetQuickPreview();return;}
            var host=Get<FrameworkElement>("QuickImageHost");
            int desired=Math.Min(InspectionPlan.MaxSide,Math.Max(256,(int)Math.Ceiling(Math.Max(host.ActualWidth,host.ActualHeight))));
            bool same=quickSource!=null && quickSource.GroupId==row.GroupId && quickSource.ContentId==row.ContentId && quickSource.RootPath==row.RootPath && quickSource.RelativePath==row.RelativePath && quickSource.FileIdentity==row.FileIdentity && quickSource.ModifiedTicks==row.ModifiedTicks;
            if(same && desired<=quickDecodeSide+32)return;
            if(!same)ResetQuickPreview();
            quickSource=row;quickDecodeSide=desired;
            loader.QueueInspect(row,inspection.Advance(),desired);
        }
        void ApplyQuickPreview(MediaLoadResult result) {
            var current=QuickCard();
            if(display!=ReviewView.Quick || current==null || result.GroupId!=current.Record.GroupId || result.ContentId!=current.Record.ContentId)return;
            if(result.Image!=null)Get<Image>("QuickDetailImage").Source=result.Image;
            else if(!String.IsNullOrEmpty(result.Error))Message(result.Error);
        }
        void MoveQuick(int direction){
            CancelQuickGesture();
            int next=quickIndex+direction;
            if(next>=0 && next<cards.Count){ResetQuickPreview();quickIndex=next;UpdateControls();return;}
            int target=page+direction;
            if(target<1 || target>pageCount)return;
            NewPage(target);quickIndex=direction<0 ? Number("PageSizeBox")-1 : 0;
        }

        void ReviewWorkspaceItems(string target) {
            if(display!=ReviewView.Quick){Review(target,Selected());return;}
            var card=QuickCard();if(card==null || !card.CanSelect)return;
            long generation=view.Current;
            Review(target,new[]{card},r=>{
                if(!r.Success || !view.Accept(generation) || display!=ReviewView.Quick)return;
                if(target==BucketKeys[bucket] && ReferenceEquals(QuickCard(),card))MoveQuick(1);
                UpdateControls();
            },stillIntended:()=>display==ReviewView.Quick && view.Accept(generation) && ReferenceEquals(QuickCard(),card));
        }

        void ReviewKey(object sender,KeyEventArgs e) {
            if(!Ready || e.Handled || Keyboard.Modifiers!=ModifierKeys.None)return;
            string direction=QuickReviewKey(e.Key);
            if(direction!=null && display==ReviewView.Quick && Get<FrameworkElement>("QuickImageHost").IsKeyboardFocusWithin){
                e.Handled=true;if(!e.IsRepeat){CancelQuickGesture();Guard(()=>ReviewWorkspaceItems(direction));}return;
            }
            if(e.IsRepeat || (e.Key!=Key.C && e.Key!=Key.N))return;
            for(var node=Keyboard.FocusedElement as DependencyObject;node!=null;node=node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                if(node is TextBoxBase || node is PasswordBox || node is ComboBox)return;
            if(display!=ReviewView.Quick && Selected().Length==0)return;
            e.Handled=true;Guard(()=>ReviewWorkspaceItems(e.Key==Key.C ? "COMPLIANT" : "NON_COMPLIANT"));
        }

        void SortColumn(object sender,RoutedEventArgs args){
            var header=args.OriginalSource as GridViewColumnHeader;
            var sort=header==null ? null : header.Tag as string;
            if(!Ready || sort==null)return;
            columnDescending=columnSort==sort ? !columnDescending : sort=="size" || sort=="modified";
            columnSort=sort;NewPage(1);
        }

        void UpdateWorkspace() {
            if(workspace==null)return;
            bool showSidebar=!workspace.SidebarCollapsed;
            Get<FrameworkElement>("QueueSidebar").Visibility=showSidebar ? Visibility.Visible : Visibility.Collapsed;
            Get<FrameworkElement>("CompactQueues").Visibility=showSidebar ? Visibility.Collapsed : Visibility.Visible;
            SetPaneTab("QueueSidebarButton",showSidebar ? "Left" : "Right",showSidebar ? "Collapse left sidebar" : "Expand left sidebar");
            Get<FrameworkElement>("TopControls").Visibility=workspace.TopCollapsed ? Visibility.Collapsed : Visibility.Visible;
            SetPaneTab("TopControlsButton",workspace.TopCollapsed ? "Down" : "Up",workspace.TopCollapsed ? "Expand top controls" : "Collapse top controls");
            for(int i=0;i<BucketKeys.Length;i++){
                var queue=Button("Queue"+i);queue.Tag=bucket==i ? "Active" : null;queue.IsEnabled=Ready;
            }
            Get<ListView>("ReviewList").Visibility=display==ReviewView.List ? Visibility.Visible : Visibility.Collapsed;
            Get<ScrollViewer>("GalleryScroll").Visibility=display==ReviewView.Thumbnails ? Visibility.Visible : Visibility.Collapsed;
            Get<Slider>("ThumbnailSize").Visibility=display==ReviewView.Thumbnails ? Visibility.Visible : Visibility.Collapsed;
            var focus=FocusedCard();var preview=Get<FrameworkElement>("PreviewPane");preview.DataContext=focus;
            bool showPreviewTab=display!=ReviewView.Quick;
            ShowPreviewPane(!workspace.PreviewCollapsed && showPreviewTab);
            Button("PreviewPaneButton").Visibility=showPreviewTab ? Visibility.Visible : Visibility.Collapsed;
            SetPaneTab("PreviewPaneButton",workspace.PreviewCollapsed ? "Left" : "Right",workspace.PreviewCollapsed ? "Expand right preview" : "Collapse right preview");
            Button("PaneDetailsButton").IsEnabled=Button("PaneInspectButton").IsEnabled=Ready && focus!=null;
            Button("PaneOpenButton").IsEnabled=Ready && focus!=null && focus.Record.Status!="EVIDENCE" && focus.Record.LocationCount>0;
            Button("PaneCaseButton").Visibility=focus!=null && focus.Record.Status=="NON_COMPLIANT" ? Visibility.Visible : Visibility.Collapsed;
            Button("PaneCaseButton").IsEnabled=Ready;
            var quick=Get<FrameworkElement>("QuickPane");quick.DataContext=display==ReviewView.Quick ? focus : null;
            quick.Visibility=display==ReviewView.Quick ? Visibility.Visible : Visibility.Collapsed;
            Button("QuickNonCompliantButton").IsEnabled=Button("QuickCompliantButton").IsEnabled=Ready && focus!=null && focus.CanSelect;
            Button("QuickInspectButton").IsEnabled=Ready && focus!=null;
            Button("QuickPreviousButton").IsEnabled=Ready && (quickIndex>0 || page>1);
            Button("QuickNextButton").IsEnabled=Ready && (quickIndex+1<cards.Count || page<pageCount);
            Button("ListModeButton").IsEnabled=display!=ReviewView.List;
            Button("ThumbnailsModeButton").IsEnabled=display!=ReviewView.Thumbnails;
            Button("QuickModeButton").IsEnabled=Ready && focus!=null && display!=ReviewView.Quick;
            Button("RefreshViewButton").IsEnabled=Ready;
        }

        void SetPaneTab(string name,string direction,string action) {
            var tab=Button(name);
            tab.Content=direction;
            tab.ToolTip=action;
            AutomationProperties.SetName(tab,action);
        }

        void ShowPreviewPane(bool show) {
            var pane=Get<FrameworkElement>("PreviewPane");
            Visibility visibility=show ? Visibility.Visible : Visibility.Collapsed;
            if(pane.Visibility==visibility)return;
            var divider=Get<GridSplitter>("PreviewDivider");
            var column=Get<ColumnDefinition>("PreviewColumn");
            if(show){
                double width=workspace.PreviewWidth;
                if(!(width>=PreviewMinWidth && width<=PreviewMaxWidth))width=PreviewDefaultWidth;
                UpdatePreviewWidthLimit();
                column.MinWidth=PreviewMinWidth;
                column.Width=new GridLength(width);
            }else{
                if(divider.IsDragging)divider.CancelDrag();
                workspace.PreviewWidth=column.Width.Value;
                column.MinWidth=0;column.MaxWidth=0;column.Width=new GridLength(0);
            }
            pane.Visibility=divider.Visibility=visibility;
        }

        void UpdatePreviewWidthLimit() {
            double width=Get<Grid>("ReviewArea").ActualWidth-Button("PreviewPaneButton").Width-330;
            Get<ColumnDefinition>("PreviewColumn").MaxWidth=Math.Max(PreviewMinWidth,Math.Min(PreviewMaxWidth,width));
        }

        void RestoreBounds() {
            if(workspace.Bounds==null || workspace.Bounds.Length!=4 || workspace.Bounds.Any(n=>Double.IsInfinity(n) || Double.IsNaN(n)))return;
            Rect screen=SystemParameters.WorkArea;
            var b=workspace.Bounds;
            Window.Width=Math.Max(Window.MinWidth,Math.Min(b[2],screen.Width));
            Window.Height=Math.Max(Window.MinHeight,Math.Min(b[3],screen.Height));
            Window.Left=Math.Max(screen.Left,Math.Min(b[0],screen.Right-Window.Width));
            Window.Top=Math.Max(screen.Top,Math.Min(b[1],screen.Bottom-Window.Height));
        }

        void SaveWorkspace() {
            if(!persistWorkspace)return;
            Rect bounds=Window.RestoreBounds;
            if(!bounds.IsEmpty)workspace.Bounds=new[]{bounds.Left,bounds.Top,bounds.Width,bounds.Height};
            workspace.TileSize=Get<Slider>("ThumbnailSize").Value;
            if(Get<GridSplitter>("PreviewDivider").Visibility==Visibility.Visible)workspace.PreviewWidth=Get<ColumnDefinition>("PreviewColumn").Width.Value;
            workspace.Save();
        }
    }
}
