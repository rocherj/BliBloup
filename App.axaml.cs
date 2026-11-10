using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using BoomBx.ViewModels;
using BoomBx.Views;
using System.Runtime.InteropServices;
using Avalonia.Logging;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;


namespace BoomBx;

public partial class App : Application
{
    // [DllImport("kernel32.dll")]
    // private static extern bool AllocConsole();

    // [DllImport("kernel32.dll")]
    // private static extern bool AttachConsole(int pid);

    public SplashScreen _splash;
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    private TrayIcon? _trayIcon;
    private NativeMenuItem? _trayNowPlayingItem;
    private NativeMenuItem? _trayHotkeysItem;


    public App()
    {
        //if (!AttachConsole(-1)) AllocConsole();
        _splash = new SplashScreen();
    }

    public void UpdateSplashStatus(string message)
    {
        _splash?.UpdateStatus(message);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            _desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _desktop.Exit += (_, _) => DisposeTrayIcon();
            InitializeTrayIcon();
            
            BindingPlugins.DataValidators.RemoveAt(0);
            
            try
            {
                _splash.Show();
                
                var mainWindow = new MainWindow();
                await mainWindow.InitializeAsync();
                
                var tcs = new TaskCompletionSource();
                
                mainWindow.SetCloseSplashAction(async () => 
                {
                    try
                    {
                        await Task.Delay(100);
                        Dispatcher.UIThread.Post(() => 
                        {
                            _desktop.MainWindow = mainWindow;
                            _desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                            mainWindow.Show();
                        });
                        await _splash.CloseSplashAsync();
                        await mainWindow.StartMainInitialization();
                    }
                    finally
                    {
                        tcs.SetResult();
                    }
                });

                await mainWindow.CloseSplashAsync(); // Trigger the close action
                await tcs.Task;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex}");
                throw;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeMainWindowAsync(MainWindow mainWindow)
    {
        await Task.Delay(100);
    }

    /// <summary>
    /// Creates the discreet system tray indicator. It confirms background playback without
    /// ever stealing the focus of the foreground application.
    /// </summary>
    private void InitializeTrayIcon()
    {
        try
        {
            // Kept open for the lifetime of the process: the tray icon reads the stream lazily
            // through the platform implementation.
            var iconStream = AssetLoader.Open(new Uri("avares://BoomBx/Assets/logo.ico"));

            var menu = new NativeMenu();

            _trayNowPlayingItem = new NativeMenuItem("Now playing: —") { IsEnabled = false };

            var showItem = new NativeMenuItem("Show BoomBx");
            showItem.Click += (_, _) => ShowMainWindow();

            var stopItem = new NativeMenuItem("Stop all sounds");
            stopItem.Click += (_, _) => WithMainWindow(window => window.StopAllSoundsFromTray());

            _trayHotkeysItem = new NativeMenuItem("Disable hotkeys");
            _trayHotkeysItem.Click += (_, _) => WithMainWindow(window => window.ToggleHotkeysFromTray());

            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += (_, _) => _desktop?.Shutdown();

            menu.Items.Add(_trayNowPlayingItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(showItem);
            menu.Items.Add(stopItem);
            menu.Items.Add(_trayHotkeysItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(exitItem);

            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(iconStream),
                ToolTipText = "BoomBx — idle",
                Menu = menu
            };

            this.SetValue(TrayIcon.IconsProperty, new TrayIcons { _trayIcon });
        }
        catch (Exception ex)
        {
            // A missing tray (headless session, unsupported desktop) must never block startup.
            Console.WriteLine($"Tray icon unavailable: {ex.Message}");
            Logger.Log($"Tray icon unavailable: {ex}");
        }
    }

    /// <summary>Updates the tray tooltip and menu entry to reflect background playback.</summary>
    public void SetNowPlaying(string? soundName)
    {
        var idle = string.IsNullOrWhiteSpace(soundName);

        Dispatcher.UIThread.Post(() =>
        {
            if (_trayIcon != null)
            {
                _trayIcon.ToolTipText = idle ? "BoomBx — idle" : $"BoomBx — ▶ {soundName}";
            }

            if (_trayNowPlayingItem != null)
            {
                _trayNowPlayingItem.Header = idle ? "Now playing: —" : $"Now playing: {soundName}";
            }
        });
    }

    /// <summary>Keeps the tray menu label in sync with the "global hotkeys" setting.</summary>
    public void SetTrayHotkeysEnabled(bool enabled)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_trayHotkeysItem != null)
            {
                _trayHotkeysItem.Header = enabled ? "Disable hotkeys" : "Enable hotkeys";
            }
        });
    }

    private void ShowMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_desktop?.MainWindow is not MainWindow window)
            {
                return;
            }

            if (!window.IsVisible)
            {
                window.Show();
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
        });
    }

    private void WithMainWindow(Action<MainWindow> action)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_desktop?.MainWindow is MainWindow window)
            {
                action(window);
            }
        });
    }

    private void DisposeTrayIcon()
    {
        try
        {
            _trayIcon?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to dispose tray icon: {ex}");
        }
        finally
        {
            _trayIcon = null;
        }
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // Cleanup code here
    }
}