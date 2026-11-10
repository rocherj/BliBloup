using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BoomBx.Views
{
    /// <summary>
    /// Compact waveform preview for the trim editor: pre-computed peaks with the selected
    /// region highlighted. Purely visual (no interaction), the sliders drive the selection.
    /// </summary>
    public class WaveformView : Control
    {
        public static readonly StyledProperty<float[]?> PeaksProperty =
            AvaloniaProperty.Register<WaveformView, float[]?>(nameof(Peaks));

        public static readonly StyledProperty<double> DurationProperty =
            AvaloniaProperty.Register<WaveformView, double>(nameof(Duration));

        public static readonly StyledProperty<double> SelectionStartProperty =
            AvaloniaProperty.Register<WaveformView, double>(nameof(SelectionStart));

        public static readonly StyledProperty<double> SelectionEndProperty =
            AvaloniaProperty.Register<WaveformView, double>(nameof(SelectionEnd));

        static WaveformView()
        {
            AffectsRender<WaveformView>(PeaksProperty, DurationProperty, SelectionStartProperty, SelectionEndProperty);
        }

        public float[]? Peaks
        {
            get => GetValue(PeaksProperty);
            set => SetValue(PeaksProperty, value);
        }

        public double Duration
        {
            get => GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        public double SelectionStart
        {
            get => GetValue(SelectionStartProperty);
            set => SetValue(SelectionStartProperty, value);
        }

        public double SelectionEnd
        {
            get => GetValue(SelectionEndProperty);
            set => SetValue(SelectionEndProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            context.FillRectangle(new SolidColorBrush(Color.Parse("#1F1F1F")), bounds, 3);

            double mid = bounds.Height / 2;
            context.DrawLine(
                new Pen(new SolidColorBrush(Color.Parse("#3A3A3A")), 1),
                new Point(0, mid),
                new Point(bounds.Width, mid));

            var peaks = Peaks;
            double duration = Duration;

            if (peaks is null || peaks.Length == 0 || duration <= 0.0001)
            {
                return;
            }

            double start = Math.Clamp(Math.Min(SelectionStart, SelectionEnd), 0, duration);
            double end = Math.Clamp(Math.Max(SelectionStart, SelectionEnd), 0, duration);
            double startX = start / duration * bounds.Width;
            double endX = end / duration * bounds.Width;

            var selectedBrush = new SolidColorBrush(Color.Parse("#007ACC"));
            var idleBrush = new SolidColorBrush(Color.Parse("#4A4A4A"));
            double barWidth = bounds.Width / peaks.Length;

            for (int i = 0; i < peaks.Length; i++)
            {
                double centerX = (i + 0.5) * barWidth;
                double height = Math.Max(1, peaks[i] * (mid - 2));
                var rectangle = new Rect(
                    i * barWidth,
                    mid - height,
                    Math.Max(1, barWidth * 0.8),
                    height * 2);

                bool isSelected = centerX >= startX && centerX <= endX;
                context.FillRectangle(isSelected ? selectedBrush : idleBrush, rectangle);
            }

            var borderPen = new Pen(new SolidColorBrush(Color.Parse("#4FC1FF")), 1.5);
            context.DrawLine(borderPen, new Point(startX, 0), new Point(startX, bounds.Height));
            context.DrawLine(borderPen, new Point(endX, 0), new Point(endX, bounds.Height));
        }
    }
}
