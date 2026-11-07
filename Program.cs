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
        bool createdNew;
        _singleInstanceMutex = new Mutex(true, MutexName, out createdNew);

        if (!createdNew)
        {
            // Si ya está corriendo en la bandeja del sistema, le avisamos que abra su ventana al frente
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

        ApplicationConfiguration.Initialize();

        bool startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        Application.Run(new Form1(startMinimized));

        _singleInstanceMutex.ReleaseMutex();
    }
}
