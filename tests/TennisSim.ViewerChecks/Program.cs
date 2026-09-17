using System;
using System.IO;
using TennisSim.Viewer.Tests;
class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 1 || args.Length > 2 || (args.Length == 2 && args[1] != "--general")) { Console.Error.WriteLine("Usage: ViewerChecks SAMPLE_JSON [--general]"); return 2; }
        int passed = 0, failed = 0;
        ReplayChecks.Run(File.ReadAllText(args[0]), (name, test) => {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
        }, args.Length == 1);
        Console.WriteLine(passed + " passed, " + failed + " failed (shared data code; NOT Unity tests)");
        return failed == 0 ? 0 : 1;
    }
}
