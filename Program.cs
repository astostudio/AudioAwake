using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SoundbarKeepAlive;

static class Program
{
#if GAMER_BUILD
    private const string MutexName = "Global\\AudioAwake_GamerEdition_Mutex";
    public const string ShowWindowEventName = "Global\\AudioAwake_Gamer_ShowWindow_Event";
#else
    private const string MutexName = "Global\\AudioAwake_SingleInstance_Mutex";
    public const string ShowWindowEventName = "Global\\AudioAwake_ShowWindow_Event";
#endif
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

        // Si el usuario abre otra edición distinta (ej. abre Gaming mientras estaba abierto el normal),
        // cerramos el proceso anterior para que se abra la edición que acaba de ejecutar.
        try
        {
            int currentPid = Environment.ProcessId;
            foreach (var proc in Process.GetProcesses())
            {
                if (proc.Id != currentPid &&
                    (proc.ProcessName.Equals("AudioAwake", StringComparison.OrdinalIgnoreCase) ||
                     proc.ProcessName.Equals("AudioAwake_PRO", StringComparison.OrdinalIgnoreCase) ||
                     proc.ProcessName.Equals("AudioAwake_Gaming", StringComparison.OrdinalIgnoreCase) ||
                     proc.ProcessName.Equals("SoundbarKeepAlive", StringComparison.OrdinalIgnoreCase)))
                {
                    // Si es exactamente el mismo ejecutable, solo mostramos su ventana
                    string? otherPath = null;
                    try { otherPath = proc.MainModule?.FileName; } catch { }
                    if (string.Equals(otherPath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                    {
                        using var evt = EventWaitHandle.OpenExisting(ShowWindowEventName);
                        evt.Set();
                        return;
                    }
                    else
                    {
                        try { proc.Kill(); proc.WaitForExit(1000); } catch { }
                    }
                }
            }
        }
        catch
        {
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
