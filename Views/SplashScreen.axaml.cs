using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace BliBloup.Views
{
    public partial class SplashScreen : Window
    {
        /// <summary>
        /// Startup is intentionally silent: the splash only covers initialization and must
        /// never play audio. A short minimum keeps it from flashing when startup is quick.
        /// </summary>
        private static readonly TimeSpan MinimumDisplayTime = TimeSpan.FromSeconds(1);

        private readonly DateTime _shownAt = DateTime.UtcNow;

        public bool IsClosed { get; private set; }

        public SplashScreen()
        {
            InitializeComponent();
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.CanResize = false;
            this.SystemDecorations = SystemDecorations.None;
        }

        public async Task CloseSplashAsync()
        {
            try
            {
                if (IsClosed)
                {
                    return;
                }

                var elapsed = DateTime.UtcNow - _shownAt;

                if (elapsed < MinimumDisplayTime)
                {
                    await Task.Delay(MinimumDisplayTime - elapsed);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!IsClosed)
                    {
                        Close();
                        IsClosed = true;
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing splash screen: {ex.Message}");
                Logger.Log($"Error closing splash screen: {ex}");
            }
        }

        public void UpdateStatus(string? message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = message ?? string.Empty;
            });
        }
    }
}
