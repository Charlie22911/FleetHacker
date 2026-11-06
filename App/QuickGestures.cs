using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MediaReviewer {
    sealed partial class ReviewerWindow {
        FrameworkElement quickGestureHost;
        TranslateTransform quickOffset;
        TouchDevice quickTouch;
        CardItem quickGestureCard;
        MediaRow quickGestureRow;
        Point quickGestureStart;
        long quickGestureGeneration;
        bool quickPanning;
        double quickPanOriginX,quickPanOriginY;

        internal static string QuickReviewKey(Key key) {
            if(key==Key.Up)return "COMPLIANT";
            return key==Key.Down ? "NON_COMPLIANT" : null;
        }

        internal static Key QuickReviewDrag(double x,double y,double width,double height) {
            double horizontal=Math.Abs(x),vertical=Math.Abs(y);
            if(horizontal>=Math.Max(45,Math.Min(120,width*0.16)) && horizontal>=1.5*vertical)
                return x<0 ? Key.Left : Key.Right;
            if(vertical>=Math.Max(32,Math.Min(95,height*0.18)) && vertical>=1.5*horizontal)
                return y<0 ? Key.Up : Key.Down;
            return Key.None;
        }

        bool CanQuickAction(Key direction) {
            if(display!=ReviewView.Quick || !Ready)return false;
            if(direction==Key.Left || direction==Key.Right)return CanMoveQuick(direction==Key.Left ? -1 : 1);
            var card=QuickCard();
            return QuickReviewKey(direction)!=null && card!=null && card.CanSelect;
        }

        void RunQuickAction(Key direction) {
            if(!CanQuickAction(direction))return;
            CancelQuickGesture();
            if(direction==Key.Left || direction==Key.Right)MoveQuick(direction==Key.Left ? -1 : 1);
            else ReviewWorkspaceItems(QuickReviewKey(direction));
        }

        void SetUpQuickGestures() {
            quickGestureHost=Get<FrameworkElement>("QuickImageHost");
            quickOffset=new TranslateTransform();
            Get<FrameworkElement>("QuickSlide").RenderTransform=quickOffset;
            quickGestureHost.PreviewMouseWheel+=(s,e)=>{
                if(!Ready || display!=ReviewView.Quick || QuickCard()==null || CanPlayQuickVideo(QuickCard().Record))return;
                e.Handled=true;
                AdjustQuickZoom(e.Delta>0 ? 1.25 : 0.8);
            };
            quickGestureHost.MouseLeftButtonDown+=(s,e)=>{
                if(e.StylusDevice!=null)return;
                if(e.ClickCount==2 && (quickZoom>1.01 || quickRotation!=0)){
                    CancelQuickGesture();ResetQuickTransform();e.Handled=true;return;
                }
                if(!BeginQuickGesture(e.GetPosition(quickGestureHost)))return;
                if(!quickGestureHost.CaptureMouse())CancelQuickGesture();
                e.Handled=true;
            };
            quickGestureHost.MouseMove+=(s,e)=>{
                if(quickGestureCard!=null && quickTouch==null && quickGestureHost.IsMouseCaptured)
                    UpdateQuickGesture(e.GetPosition(quickGestureHost));
            };
            quickGestureHost.MouseLeftButtonUp+=(s,e)=>{
                if(e.StylusDevice!=null || quickGestureCard==null || quickTouch!=null)return;
                e.Handled=true;FinishQuickGesture(e.GetPosition(quickGestureHost));
            };
            quickGestureHost.LostMouseCapture+=(s,e)=>{if(quickTouch==null)CancelQuickGesture();};
            quickGestureHost.TouchDown+=(s,e)=>{
                e.Handled=true;
                if(!BeginQuickGesture(e.GetTouchPoint(quickGestureHost).Position))return;
                quickTouch=e.TouchDevice;
                if(!quickGestureHost.CaptureTouch(quickTouch))CancelQuickGesture();
            };
            quickGestureHost.TouchMove+=(s,e)=>{
                e.Handled=true;
                if(quickTouch==e.TouchDevice)UpdateQuickGesture(e.GetTouchPoint(quickGestureHost).Position);
            };
            quickGestureHost.TouchUp+=(s,e)=>{
                e.Handled=true;
                if(quickTouch==e.TouchDevice)FinishQuickGesture(e.GetTouchPoint(quickGestureHost).Position);
            };
            quickGestureHost.LostTouchCapture+=(s,e)=>{if(quickTouch==e.TouchDevice)CancelQuickGesture();};
        }

        bool BeginQuickGesture(Point start) {
            if(!Ready || display!=ReviewView.Quick || quickGestureCard!=null)return false;
            FocusQuick();
            var card=QuickCard();
            if(card==null)return false;
            quickGestureCard=card;quickGestureRow=card.Record;quickGestureStart=start;quickGestureGeneration=view.Current;
            quickPanning=quickZoom>1.01;
            quickPanOriginX=quickPanX;quickPanOriginY=quickPanY;
            return true;
        }

        void UpdateQuickGesture(Point current) {
            var delta=current-quickGestureStart;
            if(quickPanning){
                quickPanX=quickPanOriginX+delta.X;
                quickPanY=quickPanOriginY+delta.Y;
                ApplyQuickTransform();
                return;
            }
            bool horizontal=Math.Abs(delta.X)>Math.Abs(delta.Y);
            quickOffset.X=horizontal ? Math.Max(-110,Math.Min(110,delta.X)) : 0;
            quickOffset.Y=horizontal ? 0 : Math.Max(-80,Math.Min(80,delta.Y));
            Key direction=QuickReviewDrag(delta.X,delta.Y,quickGestureHost.ActualWidth,quickGestureHost.ActualHeight);
            var badge=Get<Border>("QuickGestureBadge");
            var label=Get<TextBlock>("QuickGestureLabel");
            badge.Visibility=CanQuickAction(direction) ? Visibility.Visible : Visibility.Collapsed;
            if(badge.Visibility==Visibility.Visible) {
                switch(direction) {
                    case Key.Left:
                        badge.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
                        badge.SetResourceReference(Border.BorderBrushProperty,"FocusBrush");
                        label.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush");
                        label.Text="← Release for previous";
                        break;
                    case Key.Right:
                        badge.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
                        badge.SetResourceReference(Border.BorderBrushProperty,"FocusBrush");
                        label.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush");
                        label.Text="Release for next →";
                        break;
                    case Key.Up:
                        badge.SetResourceReference(Border.BackgroundProperty,"GoodBrush");
                        badge.SetResourceReference(Border.BorderBrushProperty,"GoodTextBrush");
                        label.SetResourceReference(TextBlock.ForegroundProperty,"GoodTextBrush");
                        label.Text="✓ Release to mark Compliant (↑)";
                        break;
                    case Key.Down:
                        badge.SetResourceReference(Border.BackgroundProperty,"BadBrush");
                        badge.SetResourceReference(Border.BorderBrushProperty,"BadTextBrush");
                        label.SetResourceReference(TextBlock.ForegroundProperty,"BadTextBrush");
                        label.Text="✕ Release to mark Non-compliant (↓)";
                        break;
                }
            }
        }

        void FinishQuickGesture(Point current) {
            if(quickPanning){
                CancelQuickGesture();
                return;
            }
            var delta=current-quickGestureStart;
            Key direction=QuickReviewDrag(delta.X,delta.Y,quickGestureHost.ActualWidth,quickGestureHost.ActualHeight);
            var card=QuickCard();
            bool currentItem=Ready && display==ReviewView.Quick && view.Accept(quickGestureGeneration) &&
                card!=null && ReferenceEquals(card,quickGestureCard) &&
                card.Record.ContentId==quickGestureRow.ContentId && card.Record.ReviewRevision==quickGestureRow.ReviewRevision;
            CancelQuickGesture();
            if(!currentItem)return;
            if(direction==Key.None && delta.Length<6 && quickPlayer!=null)quickPlayer.TogglePlay();
            else Guard(()=>RunQuickAction(direction));
        }

        void CancelQuickGesture() {
            if(quickGestureCard==null)return;
            quickGestureCard=null;quickGestureRow=null;quickPanning=false;
            var touch=quickTouch;quickTouch=null;
            if(touch!=null)quickGestureHost.ReleaseTouchCapture(touch);
            if(quickGestureHost.IsMouseCaptured)quickGestureHost.ReleaseMouseCapture();
            quickOffset.X=quickOffset.Y=0;
            Get<Border>("QuickGestureBadge").Visibility=Visibility.Collapsed;
        }
    }
}
