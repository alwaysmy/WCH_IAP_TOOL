namespace WCHIAPToolNew;

static class Program
{
    public static bool DebugMode = false;

    [STAThread]
    static void Main(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg.Equals("--debug", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-d", StringComparison.OrdinalIgnoreCase))
            {
                DebugMode = true;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }    
}
