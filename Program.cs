using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SoundbarKeepAlive;

static class Program
{
    private const string MutexName = "Global\\AudioAwake_SingleInstance_Mutex";
    public const string ShowWindowEventName = "Global\\AudioAwake_ShowWindow_Event";
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Any(a => a.Equals("--export-images", StringComparison.OrdinalIgnoreCase)))
        {
            using var exporter = new Form1(startMinimized: true);
            exporter.ExportStoreMarketingImages();
            return;
        }

        bool createdNew;
        _singleInstanceMutex = new Mutex(true, MutexName, out createdNew);

        if (!createdNew)
        {
            try
            {
                using var evt = EventWaitHandle.OpenExisting(ShowWindowEventName);
                evt.Set();
            }
            catch
            {
            }
            return;
        }

        bool startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        Application.Run(new Form1(startMinimized));

        _singleInstanceMutex.ReleaseMutex();
    }
}
