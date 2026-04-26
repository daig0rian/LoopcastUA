using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using LoopcastUA.Config;
using LoopcastUA.Infrastructure;

namespace LoopcastUA
{
    static class Program
    {
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);

        private static Mutex _mutex;

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--encrypt-config")
            {
                RunEncryptConfig();
                return;
            }

            _mutex = new Mutex(true, @"Global\LoopcastUA_Instance", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(Strings.AlreadyRunning, Strings.AppTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _mutex.Dispose();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            timeBeginPeriod(1);
            try
            {
                Application.Run(new TrayContext());
            }
            finally
            {
                timeEndPeriod(1);
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }

        private static void RunEncryptConfig()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LoopcastUA", "config.json");
            if (!File.Exists(path)) return;
            try
            {
                var store = new ConfigStore();
                store.Load(path);
                store.Save(path, store.Current);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("encrypt-config failed: " + ex.Message);
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            try { Logger.Error("Unhandled UI thread exception: " + e.Exception); } catch { }
            MessageBox.Show(
                Strings.UnexpectedError + e.Exception.Message,
                Strings.AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try { Logger.Error("Fatal unhandled exception: " + e.ExceptionObject); } catch { }
        }
    }
}
