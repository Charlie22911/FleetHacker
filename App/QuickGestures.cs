using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MediaReviewer {
    sealed partial class ReviewerWindow {
        const string QuickHint="Swipe, drag, or use the arrow keys.";
        FrameworkElement quickGestureHost;
        TranslateTransform quickOffset;
        TouchDevice quickTouch;
        CardItem quickGestureCard;
        MediaRow quickGestureRow;
        Point quickGestureStart;
        long quickGestureGeneration;

        internal static string QuickReviewKey(Key key) {
            if(key==Key.Left)return "NON_COMPLIANT";
            return key==Key.Right ? "COMPLIANT" : null;
        }

        internal static string QuickReviewDrag(double x,double y,double width) {
            double threshold=Math.Max(60,Math.Min(130,width*0.18));
            if(Math.Abs(x)<threshold || Math.Abs(x)<1.5*Math.Abs(y))return null;
            return QuickReviewKey(x<0 ? Key.Left : Key.Right);
        }

        void SetUpQuickGestures() {
            quickGestureHost=Get<FrameworkElement>("QuickImageHost");
            quickOffset=new TranslateTransform();
            Get<FrameworkElement>("QuickSlide").RenderTransform=quickOffset;
            quickGestureHost.MouseLeftButtonDown+=(s,e)=>{
                if(e.StylusDevice!=null || !BeginQuickGesture(e.GetPosition(quickGestureHost)))return;
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
            if(card==null || !card.CanSelect)return false;
            quickGestureCard=card;quickGestureRow=card.Record;quickGestureStart=start;quickGestureGeneration=view.Current;
            return true;
        }

        void UpdateQuickGesture(Point current) {
            var delta=current-quickGestureStart;
            quickOffset.X=Math.Max(-180,Math.Min(180,delta.X));
            string destination=QuickReviewDrag(delta.X,delta.Y,quickGestureHost.ActualWidth);
            Text("QuickLabel",destination==null ? QuickHint : destination=="COMPLIANT" ? "Release for Compliant" : "Release for Non-compliant");
        }

        void FinishQuickGesture(Point current) {
            var delta=current-quickGestureStart;
            string destination=QuickReviewDrag(delta.X,delta.Y,quickGestureHost.ActualWidth);
            var card=QuickCard();
            bool currentItem=Ready && display==ReviewView.Quick && view.Accept(quickGestureGeneration) &&
                card!=null && ReferenceEquals(card,quickGestureCard) && card.CanSelect &&
                card.Record.ContentId==quickGestureRow.ContentId && card.Record.ReviewRevision==quickGestureRow.ReviewRevision;
            CancelQuickGesture();
            if(currentItem && destination!=null)Guard(()=>ReviewWorkspaceItems(destination));
        }

        void CancelQuickGesture() {
            if(quickGestureCard==null)return;
            quickGestureCard=null;quickGestureRow=null;
            var touch=quickTouch;quickTouch=null;
            if(touch!=null)quickGestureHost.ReleaseTouchCapture(touch);
            if(quickGestureHost.IsMouseCaptured)quickGestureHost.ReleaseMouseCapture();
            quickOffset.X=0;
            Text("QuickLabel",QuickHint);
        }
    }
}
