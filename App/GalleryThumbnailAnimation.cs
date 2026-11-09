using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace MediaReviewer {
    sealed partial class ReviewerWindow {
        readonly GalleryAnimations galleryAnimations=new GalleryAnimations();
        DispatcherTimer galleryAnimationTimer;DateTime galleryVisibilityAt;
        void SetUpGalleryAnimations(){
            galleryAnimationTimer=new DispatcherTimer(DispatcherPriority.Background,Window.Dispatcher){Interval=TimeSpan.FromMilliseconds(20)};
            galleryAnimationTimer.Tick+=(s,e)=>{
                if(closing || !Ready || display!=ReviewView.Thumbnails || !Window.IsVisible || Window.WindowState==WindowState.Minimized){if(galleryAnimations.ActiveCount>0)galleryAnimations.Clear();return;}
                if(DateTime.UtcNow>=galleryVisibilityAt){galleryAnimations.SetVisible(VisibleGalleryCards());galleryVisibilityAt=DateTime.UtcNow.AddMilliseconds(100);}
                galleryAnimations.Tick();
            };
            galleryAnimationTimer.Start();
        }
        IEnumerable<CardItem> VisibleGalleryCards(){
            var scroll=Get<ScrollViewer>("GalleryScroll");var list=Get<ItemsControl>("CardList");
            var viewport=new Rect(0,0,scroll.ViewportWidth,scroll.ViewportHeight);
            if(viewport.Width<=0 || viewport.Height<=0)yield break;
            foreach(var card in cards){
                var container=list.ItemContainerGenerator.ContainerFromItem(card) as FrameworkElement;
                if(container==null || !container.IsVisible || container.ActualHeight<=0)continue;
                bool visible=false;
                try{visible=container.TransformToAncestor(scroll).TransformBounds(new Rect(container.RenderSize)).IntersectsWith(viewport);}catch(InvalidOperationException){}
                if(visible)yield return card;
            }
        }
    }
}
