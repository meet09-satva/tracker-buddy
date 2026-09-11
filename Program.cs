namespace TrackerBuddy;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Velopack.VelopackApp.Build().Run(); // handles Setup.exe install/update/uninstall hooks; must run first

        if (args.Contains("--test"))
        {
            try { Calc.SelfTest(); Console.WriteLine("All checks passed"); return 0; }
            catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }
        }

        using var single = new Mutex(true, "TrackerBuddy.SingleInstance", out var first);
        if (!first) return 0;

        ApplicationConfiguration.Initialize();
        Application.Run(new Widget());
        return 0;
    }
}
