using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaReviewer {
    internal static class GemmaCriteria {
        internal const int Limit=900;
        internal const string Default=
            "A: Exposed breasts, genitals, bare buttocks, sexual activity, or clearly implied toplessness where arms or objects cover the breasts. This includes artistic or non-pornographic nudity.\n"+
            "B: Sexualized lingerie or underwear posing, including a buttocks-focused close-up, even with no nudity.\n"+
            "C: Ordinary swimwear or bikini with a visible garment covering the relevant areas, no nudity, and no sexualized framing.\n"+
            "D: Ordinary benign content such as maps, diagrams, landscapes, objects, or normally clothed people.\n"+
            "E: The image is too unclear to distinguish a plausible A or B case from an ordinary image.\n"+
            "If a person's chest is obscured, check for a visible top before choosing C.";

        internal static string Normalize(string text){
            if(String.IsNullOrWhiteSpace(text))return "";
            if(text.Length>Limit)throw new ArgumentException("Keep the Gemma criteria within "+Limit+" characters.");
            var lines=text.Replace("\r\n","\n").Replace('\r','\n').Split('\n')
                .Select(line=>Regex.Replace(line.Trim(),@"\s+"," ")).Where(line=>line.Length!=0);
            string normalized=String.Join("\n",lines);
            if(Regex.IsMatch(normalized,@"[\p{Cc}\p{Cf}-[\n]]"))throw new ArgumentException("Remove control characters from the Gemma criteria.");
            var labels=Regex.Matches(normalized,@"^([A-E]): +\S",RegexOptions.Multiline);
            if(labels.Count!=5 || !labels.Cast<Match>().Select(match=>match.Groups[1].Value[0]).SequenceEqual("ABCDE"))
                throw new ArgumentException("Keep one line for each category, labeled A: through E: in order, with a description after each label.");
            normalized=normalized.Replace('<','[').Replace('>',']');
            return normalized==Default ? "" : normalized;
        }

        internal static string Prompt(string criteria){return (String.IsNullOrEmpty(criteria) ? Default : criteria).Replace('\n',' ');}

        internal static string Model(string criteria){
            if(String.IsNullOrEmpty(criteria))return DraftReview.Gemma;
            using(var sha=SHA256.Create())
                return DraftReview.Gemma+"-criteria-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(criteria))).Replace("-","");
        }
    }
}
