using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
class CompanionFixture {
    static int Main(){
        string directory=AppDomain.CurrentDomain.BaseDirectory;
        string input=Console.ReadLine();
        File.WriteAllText(Path.Combine(directory,"fixture.pid"),Process.GetCurrentProcess().Id.ToString());
        string mode=File.ReadAllText(Path.Combine(directory,"fixture-mode.txt"));
        if(mode=="hang"){Thread.Sleep(Timeout.Infinite);return 0;}
        if(mode=="oversized"){Console.WriteLine(new string('x',65537));return 0;}
        if(mode=="crash")return 7;
        string nonce=input==null ? "" : input.Substring(10);
        Console.WriteLine("{\"Product\":\"FleetHacker.Steganography\",\"ProtocolVersion\":99,\"Nonce\":\""+nonce+"\",\"Engines\":[]}");return 0;
    }
}
