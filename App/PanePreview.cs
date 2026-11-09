using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MediaReviewer {
    sealed partial class ReviewerWindow {
        readonly MediaLoader paneLoader=new MediaLoader(true);
        readonly ViewGeneration paneGeneration=new ViewGeneration();
        MediaRow paneSource;int paneDecodeSide;
        DateTime paneSizeChanged=DateTime.MinValue;
        int paneDesired;
        void ResetPanePreview(){
            paneLoader.CancelInspect(paneGeneration.Advance());paneSource=null;paneDecodeSide=0;paneDesired=0;
            Get<Image>("PaneDetailImage").Source=null;Text("PanePreviewStatus","");
        }
        void LoadPanePreview(){
            var pane=Get<FrameworkElement>("PreviewPane");var card=FocusedCard();var row=card==null ? null : card.Record;
            if(!Ready || display==ReviewView.Quick || !pane.IsVisible || row==null){if(paneSource!=null)ResetPanePreview();return;}
            bool same=SamePreviewSource(paneSource,row);
            if(!same)ResetPanePreview();
            if(row.Status=="EVIDENCE" || row.SourceIsHistorical || String.IsNullOrEmpty(row.RootPath) || String.IsNullOrEmpty(row.RelativePath)){
                Text("PanePreviewStatus","Saved preview");return;
            }
            var image=Get<Image>("PaneDetailImage");var source=PresentationSource.FromVisual(image);double dpi=source==null || source.CompositionTarget==null ? 1 : Math.Max(source.CompositionTarget.TransformToDevice.M11,source.CompositionTarget.TransformToDevice.M22);
            var host=image.Parent as FrameworkElement;
            int desired=Math.Min(InspectionPlan.MaxSide,Math.Max(256,(int)Math.Ceiling(Math.Max(host==null ? image.ActualWidth : host.ActualWidth,host==null ? image.ActualHeight : host.ActualHeight)*dpi)));
            if(!same){paneSource=row;paneSizeChanged=DateTime.UtcNow;}
            if(desired!=paneDesired){paneDesired=desired;paneSizeChanged=DateTime.UtcNow;}
            if(same && (desired<=paneDecodeSide+32 || (DateTime.UtcNow-paneSizeChanged).TotalMilliseconds<200))return;
            paneDecodeSide=desired;Text("PanePreviewStatus","Loading source preview…");
            paneLoader.QueueInspect(row,paneGeneration.Advance(),desired);
        }
        void DrainPanePreview(){
            MediaLoadResult result;
            while(paneLoader.TryTakeResult(out result)){
                var card=FocusedCard();
                if(!paneGeneration.Accept(result.Generation) || paneSource==null || card==null || !SamePreviewSource(paneSource,card.Record) || result.GroupId!=paneSource.GroupId || result.ContentId!=paneSource.ContentId)continue;
                Get<Image>("PaneDetailImage").Source=result.Image;
                Text("PanePreviewStatus",result.Image==null ? "Saved preview — source unavailable" : "Source "+result.SourceWidth+" × "+result.SourceHeight+" · preview "+result.Image.PixelWidth+" × "+result.Image.PixelHeight);
                Get<TextBlock>("PanePreviewStatus").ToolTip=result.Error;
            }
        }
    }
}
