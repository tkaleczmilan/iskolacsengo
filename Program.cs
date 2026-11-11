namespace IskolaCsengo;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "IskolaCsengo_v2_EgyPeldany", out bool created);
        if (!created)
        {
            MessageBox.Show("Az Iskolacsengő már fut.\nKeresd a tálcán, az óra mellett (csengő ikon).",
                "Iskolacsengő", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Contains("--tray")));
    }
}
