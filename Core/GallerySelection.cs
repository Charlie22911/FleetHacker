using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace MediaReviewer {
    // All state and events belong to the WPF Dispatcher; only the visible page is inspected.
    public sealed class GallerySelection:IDisposable {
        readonly ScrollViewer scroll;readonly ItemsControl list;readonly ObservableCollection<CardItem> cards;
        readonly DispatcherTimer timer;CardItem anchor;long anchorContent;Point origin;HashSet<CardItem> initial;
        bool additive,moved,disposed;SelectionAdorner adorner;AdornerLayer layer;
        public bool Updating {get;private set;}public bool Dragging {get;private set;}public event EventHandler Changed;
        public GallerySelection(ScrollViewer scroll,ItemsControl list,ObservableCollection<CardItem> cards){
            this.scroll=scroll;this.list=list;this.cards=cards;scroll.PreviewMouseLeftButtonDown+=Down;scroll.PreviewMouseMove+=Move;scroll.PreviewMouseLeftButtonUp+=Up;scroll.LostMouseCapture+=Lost;scroll.PreviewKeyDown+=Key;cards.CollectionChanged+=Collection;
            timer=new DispatcherTimer(DispatcherPriority.Background,scroll.Dispatcher){Interval=TimeSpan.FromMilliseconds(50)};timer.Tick+=Tick;
        }
        void SetAnchor(CardItem card){anchor=card;anchorContent=card.Record.ContentId;}
        void Apply(Func<CardItem,bool> selected){Updating=true;try{foreach(var card in cards)if(card.CanSelect)card.IsSelected=selected(card);}finally{Updating=false;}if(Changed!=null)Changed(this,EventArgs.Empty);}
        public void Choose(CardItem card,bool shift,bool control){
            if(disposed || card==null || !card.CanSelect || !cards.Contains(card))return;
            if(shift && anchor!=null && cards.Contains(anchor) && anchor.Record.ContentId==anchorContent){int a=cards.IndexOf(anchor),b=cards.IndexOf(card),lo=Math.Min(a,b),hi=Math.Max(a,b);var range=new HashSet<CardItem>(cards.Skip(lo).Take(hi-lo+1));Apply(c=>range.Contains(c) || control && c.IsSelected);}
            else {bool was=card.IsSelected;Apply(c=>Object.ReferenceEquals(c,card) ? !control || !was : control && c.IsSelected);SetAnchor(card);}
        }
        public void BeginDrag(Point position,bool control){if(disposed)return;EndDrag();origin=new Point(position.X,position.Y+scroll.VerticalOffset);additive=control;initial=new HashSet<CardItem>(cards.Where(c=>c.IsSelected));moved=false;Dragging=true;}
        public void DragTo(Point position){
            if(!Dragging || disposed)return;var end=new Point(position.X,position.Y+scroll.VerticalOffset);if(!moved && Math.Abs(end.X-origin.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(end.Y-origin.Y)<SystemParameters.MinimumVerticalDragDistance)return;moved=true;
            var area=new Rect(origin,end);var selected=new HashSet<CardItem>();foreach(var card in cards){var element=list.ItemContainerGenerator.ContainerFromItem(card) as FrameworkElement;if(element==null || !card.CanSelect)continue;Rect bounds=element.TransformToAncestor(scroll).TransformBounds(new Rect(element.RenderSize));bounds.Offset(0,scroll.VerticalOffset);if(area.IntersectsWith(bounds))selected.Add(card);}
            Apply(c=>selected.Contains(c) || additive && initial.Contains(c));if(adorner==null){layer=AdornerLayer.GetAdornerLayer(scroll);if(layer!=null){adorner=new SelectionAdorner(scroll);layer.Add(adorner);}}if(adorner!=null){area.Offset(0,-scroll.VerticalOffset);adorner.Area=area;adorner.InvalidateVisual();}
        }
        public void EndDrag(){if(!Dragging)return;Dragging=false;timer.Stop();if(layer!=null && adorner!=null)layer.Remove(adorner);layer=null;adorner=null;initial=null;if(scroll.IsMouseCaptured)scroll.ReleaseMouseCapture();}
        static DependencyObject Parent(DependencyObject value){var content=value as FrameworkContentElement;if(content!=null)return content.Parent;return value is Visual || value is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(value) : LogicalTreeHelper.GetParent(value);}
        void Down(object sender,MouseButtonEventArgs e){
            if(disposed || e.Handled || e.ChangedButton!=MouseButton.Left)return;CardItem card=null;CheckBox check=null;
            for(var node=e.OriginalSource as DependencyObject;node!=null && node!=scroll;node=Parent(node)){
                if(node is ScrollBar)return;if(node is CheckBox)check=(CheckBox)node;else if(node is ButtonBase || node is TextBoxBase)return;
                var element=node as FrameworkElement;if(element!=null && element.DataContext is CardItem)card=(CardItem)element.DataContext;
            }
            bool shift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0,control=(Keyboard.Modifiers&ModifierKeys.Control)!=0;
            if(card!=null){if(check!=null && !shift){if(card.CanSelect)SetAnchor(card);return;}Choose(card,shift,control);if(check!=null)e.Handled=true;return;}
            BeginDrag(e.GetPosition(scroll),control);if(!scroll.CaptureMouse()){EndDrag();return;}timer.Start();e.Handled=true;
        }
        void Move(object sender,MouseEventArgs e){if(Dragging){if(e.LeftButton!=MouseButtonState.Pressed){EndDrag();return;}DragTo(e.GetPosition(scroll));e.Handled=true;}}
        void Up(object sender,MouseButtonEventArgs e){if(Dragging){if(!moved && !additive)Apply(c=>false);EndDrag();e.Handled=true;}}
        void Lost(object sender,MouseEventArgs e){if(Dragging && !scroll.IsMouseCaptured)EndDrag();}
        void Key(object sender,KeyEventArgs e){if(Dragging && e.Key==System.Windows.Input.Key.Escape){EndDrag();e.Handled=true;}}
        void ScrollDrag(Point p){DragTo(p);if(!Dragging || !moved)return;if(p.Y<24)scroll.ScrollToVerticalOffset(Math.Max(0,scroll.VerticalOffset-24));else if(p.Y>scroll.ActualHeight-24)scroll.ScrollToVerticalOffset(scroll.VerticalOffset+24);}
        void Tick(object sender,EventArgs e){if(!Dragging || Mouse.LeftButton!=MouseButtonState.Pressed){EndDrag();return;}ScrollDrag(Mouse.GetPosition(scroll));}
        void Collection(object sender,NotifyCollectionChangedEventArgs e){EndDrag();anchor=null;}
        public void Dispose(){if(disposed)return;EndDrag();disposed=true;timer.Tick-=Tick;scroll.PreviewMouseLeftButtonDown-=Down;scroll.PreviewMouseMove-=Move;scroll.PreviewMouseLeftButtonUp-=Up;scroll.LostMouseCapture-=Lost;scroll.PreviewKeyDown-=Key;cards.CollectionChanged-=Collection;Changed=null;}
        sealed class SelectionAdorner:Adorner {
            internal Rect Area;internal SelectionAdorner(UIElement owner):base(owner){IsHitTestVisible=false;ClipToBounds=true;}
            protected override void OnRender(DrawingContext dc){dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40,30,110,210)),new Pen(Brushes.DodgerBlue,1),Area);dc.Pop();}
        }
    }
}
