using System;
using System.Linq;
namespace MediaReviewer {
    public sealed class FormatFamily {
        public string Key,Label; public string[] Extensions;
    }
    public static class Formats {
        static FormatFamily Family(string key,string label,string extensions) { return new FormatFamily { Key=key,Label=label,Extensions=extensions.Split(',') }; }
        static readonly FormatFamily[] families={
            Family("jpeg","JPEG/JFIF","jpg,jpeg,jpe,jfif"),Family("png","PNG","png"),Family("gif","GIF","gif"),Family("bmp","BMP","bmp"),Family("webp","WebP","webp"),
            Family("avif","AVIF","avif"),Family("tiff","TIFF","tif,tiff"),Family("heif","HEIC/HEIF","heic,heif"),Family("ico","ICO","ico"),Family("svg","SVG","svg"),
            Family("raw","Camera RAW","dng,cr2,cr3,nef,nrw,arw,raf,orf,rw2,pef,x3f"),Family("video","Videos","mp4,m4v,mov,avi,wmv,mkv,webm,mpg,mpeg,mpe,mts,m2ts,ts,flv")
        };
        public static FormatFamily[] Families { get { return families.Select(f=>Family(f.Key,f.Label,String.Join(",",f.Extensions))).ToArray(); } }
        public static string[] Deep { get { return families.SelectMany(f=>f.Extensions).ToArray(); } }
        public static string[] Quick { get { return families.Where(f=>f.Key!="raw").SelectMany(f=>f.Extensions).ToArray(); } }
        public static bool Supported(string extension) { return families.Any(f=>f.Extensions.Contains(extension)); }
        public static bool IsVideo(string extension) { return families.Last().Extensions.Contains(extension); }
        internal static bool UsesPlaybackProxy(string extension) { return extension=="flv" || extension=="gif" || extension=="webm"; }
        internal static bool IsPlayable(string extension) { return IsVideo(extension) || extension=="gif"; }
        public static bool IsRaw(string extension) { return families.First(f=>f.Key=="raw").Extensions.Contains(extension); }
        internal static bool IncludesDetected(string type,string[] extensions) {
            if(extensions.Contains(type))return true;
            switch(type){
                case "jpeg":return extensions.Any(x=>x=="jpg" || x=="jpe" || x=="jfif");
                case "tiff":return extensions.Contains("tif");
                case "heif":return extensions.Contains("heic");
                case "mp4":return extensions.Any(x=>x=="m4v" || x=="mov");
                case "mkv":return extensions.Contains("webm");
                case "mpeg":return extensions.Any(x=>x=="mpg" || x=="mpe");
                case "ts":return extensions.Any(x=>x=="mts" || x=="m2ts");
                case "m2ts":return extensions.Any(x=>x=="mts" || x=="ts");
                default:return false;
            }
        }
        public static string[] Filter(string key) {
            if(String.IsNullOrEmpty(key)) return new string[0];
            if(key=="image") return families.Where(f=>f.Key!="video" && f.Key!="raw").SelectMany(f=>f.Extensions).ToArray();
            var family=families.FirstOrDefault(f=>f.Key==key); if(family!=null) return (string[])family.Extensions.Clone();
            if(Supported(key)) return new[]{key}; throw new ArgumentException("Unknown media type filter");
        }
    }
}
