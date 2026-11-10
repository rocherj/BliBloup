using Avalonia;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace BliBloup;

sealed class Program
{
    private const string InstanceMutexName = "BliBloup.SingleInstance";
    private const int SwRestore = 9;
    private const int SwShow = 5;

    // Kept for the whole process lifetime: if this handle were collected the mutex would be
    // released and a second instance could start while this one is still running.
    private static Mutex? _instanceMutex;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        if (!TryAcquireSingleInstance())
        {
            ActivateExistingInstance();
            return 0;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        GC.KeepAlive(_instanceMutex);
        return 0;
    }

    /// <summary>
    /// One BliBloup per user session: two instances would install two global keyboard hooks and
    /// fight over the same hotkeys and tray icon.
    /// </summary>
    private static bool TryAcquireSingleInstance()
    {
        try
        {
            // The BLIBLOUP_INSTANCE_NAME override lets automated tests run an isolated instance.
            var instanceName = Environment.GetEnvironmentVariable("BLIBLOUP_INSTANCE_NAME");

            _instanceMutex = new Mutex(
                initiallyOwned: true,
                string.IsNullOrWhiteSpace(instanceName) ? InstanceMutexName : instanceName,
                out var createdNew);

            if (createdNew)
            {
                return true;
            }

            _instanceMutex.Dispose();
            _instanceMutex = null;
            return false;
        }
        catch (Exception ex)
        {
            // Never block startup because the mutex could not be created.
            Logger.Log($"Single instance check unavailable: {ex.Message}");
            return true;
        }
    }

    private static void ActivateExistingInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var handle = FindWindow(null, "BliBloup");

            if (handle == IntPtr.Zero)
            {
                return;
            }

            if (IsIconic(handle))
            {
                ShowWindow(handle, SwRestore);
            }
            else
            {
                ShowWindow(handle, SwShow);
            }

            SetForegroundWindow(handle);
        }
        catch (Exception ex)
        {
            Logger.Log($"Could not activate the running instance: {ex.Message}");
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
}
