using System;
using System.Threading;
using System.Windows.Forms;
using SpindleRecovery.UI;

namespace SpindleRecovery
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => ShowCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowCrash(e.ExceptionObject as Exception);
            Theme.Init();

            using (var mutex = new Mutex(true, "SpindleRecovery.SingleInstance", out bool first))
            {
                if (!first)
                {
                    MessageBox.Show("Spindle Recovery is already running.", "Spindle Recovery", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try
                {
                    // Extract the embedded engines up front so failures are reported clearly.
                    var _ = Engine.EngineHost.TestDiskPath;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("The recovery engines could not be prepared:\n\n" + ex.Message, "Spindle Recovery", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Application.Run(new MainForm(args));
            }
        }

        static void ShowCrash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                MessageBox.Show("An unexpected error occurred:\n\n" + ex.Message + "\n\n" + ex.StackTrace, "Spindle Recovery", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
