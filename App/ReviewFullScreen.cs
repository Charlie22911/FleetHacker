using System.Windows;

namespace MediaReviewer {
    internal sealed class ReviewFullScreen {
        readonly Window window;
        WindowStyle style;
        ResizeMode resize;
        WindowState state;
        internal bool Active {get;private set;}

        internal ReviewFullScreen(Window window){this.window=window;}

        internal void Set(bool active){
            if(active==Active)return;
            if(active){style=window.WindowStyle;state=window.WindowState;resize=window.ResizeMode;}
            window.WindowState=WindowState.Normal;
            window.WindowStyle=active ? WindowStyle.None : style;
            window.ResizeMode=active ? ResizeMode.NoResize : resize;
            window.WindowState=active ? WindowState.Maximized : state;
            Active=active;
        }
    }
}
