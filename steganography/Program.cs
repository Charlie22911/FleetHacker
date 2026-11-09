using System;
using System.Web.Script.Serialization;
namespace FleetHacker.Steganography {
    static class Program {
        static int Main(string[] args){
            if(args.Length!=1 || args[0]!="--capabilities")return 2;
            // The parent assigns process limits before releasing this input gate.
            var gate=new System.Text.StringBuilder();int ch;
            while((ch=Console.In.Read())!=-1 && ch!='\n'){if(gate.Length>=128)return 2;gate.Append((char)ch);}
            string request=gate.ToString().TrimEnd('\r');
            if(!request.StartsWith("FH-STEG-1 ",StringComparison.Ordinal))return 2;
            string nonce=request.Substring(10);Guid parsed;if(!Guid.TryParseExact(nonce,"N",out parsed))return 2;
            Func<string,string[],object> engine=(id,formats)=>new {Id=id,State="unavailable",Reason="Validated engine components are not present in this package.",Formats=formats};
            Console.Out.WriteLine(new JavaScriptSerializer().Serialize(new {
                Product="FleetHacker.Steganography",ProtocolVersion=1,Nonce=nonce,
                Engines=new[]{engine("aletheia",new[]{"png","jpeg","bmp"}),engine("zsteg",new[]{"png","bmp"}),engine("binwalk",new[]{"png","jpeg","bmp"})}
            }));return 0;
        }
    }
}
