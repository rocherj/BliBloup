using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;

namespace BliBloup.Views
{
    /// <summary>
    /// Lightweight, dependency-free toast shown when a sound starts while BliBloup is in the
    /// background. It never takes focus (ShowActivated=false) and closes itself.
    /// </summary>
    public partial class ToastWindow : Window
    {
        private static readonly TimeSpan DisplayTime = TimeSpan.FromSeconds(2.4);

        private readonly DispatcherTimer _hideTimer;

        public ToastWindow()
        {
            InitializeComponent();

            _hideTimer = new DispatcherTimer { Interval = DisplayTime };
            _hideTimer.Tick += (_, _) =>
            {
                _hideTimer.Stop();
                Hide();
            };
        }

        public void ShowToast(string soundName)
        {
            TitleText.Text = string.IsNullOrWhiteSpace(soundName) ? "Playing sound" : soundName;

            PositionBottomRight();

            if (!IsVisible)
            {
                Show();
            }

            _hideTimer.Stop();
            _hideTimer.Start();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            PositionBottomRight();
        }

        private void PositionBottomRight()
        {
            var screen = Screens.Primary ?? Screens.All.FirstOrDefault();

            if (screen is null)
            {
                return;
            }

            var area = screen.WorkingArea;
            Position = new Avalonia.PixelPoint(
                Math.Max(area.X, area.Right - (int)Width - 18),
                Math.Max(area.Y, area.Bottom - (int)Height - 18));
        }
    }
}
