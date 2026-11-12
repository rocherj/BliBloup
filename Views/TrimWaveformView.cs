using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace BliBloup.Views
{
    /// <summary>
    /// Interactive waveform for the trim editor. The selection is drawn as a highlighted region
    /// with two blue handles that can be dragged, and dragging anywhere else on the waveform
    /// creates a new selection. A click without movement places the playhead instead.
    /// </summary>
    public class TrimWaveformView : Control
    {
        private const double HandleHitWidth = 7;
        private const double DragThresholdPixels = 3;
        private const double HandleBarWidth = 4;

        private enum DragMode
        {
            None,
            Start,
            End,
            NewSelection
        }

        public static readonly StyledProperty<float[]?> PeaksProperty =
            AvaloniaProperty.Register<TrimWaveformView, float[]?>(nameof(Peaks));

        public static readonly StyledProperty<double> DurationProperty =
            AvaloniaProperty.Register<TrimWaveformView, double>(nameof(Duration));

        public static readonly StyledProperty<double> SelectionStartProperty =
            AvaloniaProperty.Register<TrimWaveformView, double>(
                nameof(SelectionStart),
                defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<double> SelectionEndProperty =
            AvaloniaProperty.Register<TrimWaveformView, double>(
                nameof(SelectionEnd),
                defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<double> PlayheadSecondsProperty =
            AvaloniaProperty.Register<TrimWaveformView, double>(nameof(PlayheadSeconds));

        public static readonly StyledProperty<double> MinimumSelectionSecondsProperty =
            AvaloniaProperty.Register<TrimWaveformView, double>(nameof(MinimumSelectionSeconds), 0.1);

        private DragMode _dragMode;
        private int _pointerId;
        private Point _pressPoint;
        private double _anchorTime;
        private bool _hasMoved;

        static TrimWaveformView()
        {
            AffectsRender<TrimWaveformView>(
                PeaksProperty,
                DurationProperty,
                SelectionStartProperty,
                SelectionEndProperty,
                PlayheadSecondsProperty,
                MinimumSelectionSecondsProperty);
        }

        public event EventHandler? SelectionChanged;

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

        public double PlayheadSeconds
        {
            get => GetValue(PlayheadSecondsProperty);
            set => SetValue(PlayheadSecondsProperty, value);
        }

        public double MinimumSelectionSeconds
        {
            get => GetValue(MinimumSelectionSecondsProperty);
            set => SetValue(MinimumSelectionSecondsProperty, value);
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

            double duration = Duration;

            if (!double.IsFinite(duration) || duration <= 0.0001)
            {
                return;
            }

            double selectionStart = Math.Clamp(Math.Min(SelectionStart, SelectionEnd), 0, duration);
            double selectionEnd = Math.Clamp(Math.Max(SelectionStart, SelectionEnd), 0, duration);
            double startX = selectionStart / duration * bounds.Width;
            double endX = selectionEnd / duration * bounds.Width;

            var peaks = Peaks;

            if (peaks is { Length: > 0 })
            {
                var selectedBrush = new SolidColorBrush(Color.Parse("#007ACC"));
                var idleBrush = new SolidColorBrush(Color.Parse("#4A4A4A"));
                double barWidth = bounds.Width / peaks.Length;

                if (barWidth > 0)
                {
                    for (int i = 0; i < peaks.Length; i++)
                    {
                        double centerX = (i + 0.5) * barWidth;
                        double peak = double.IsFinite(peaks[i]) ? Math.Clamp(peaks[i], 0f, 1f) : 0;
                        double height = Math.Max(1, peak * (mid - 2));
                        var rectangle = new Rect(
                            i * barWidth,
                            mid - height,
                            Math.Max(1, barWidth * 0.8),
                            height * 2);

                        bool isSelected = centerX >= startX && centerX <= endX;
                        context.FillRectangle(isSelected ? selectedBrush : idleBrush, rectangle);
                    }
                }
            }

            var handleBrush = new SolidColorBrush(Color.Parse("#007ACC"));
            var handlePen = new Pen(handleBrush, 1.5);

            // Playhead: a thin line, drawn under the handles so the selection stays readable.
            double playhead = PlayheadSeconds;

            if (double.IsFinite(playhead) && playhead >= 0 && playhead <= duration)
            {
                double playheadX = playhead / duration * bounds.Width;
                context.DrawLine(
                    new Pen(new SolidColorBrush(Color.Parse("#E8E8E8")), 1),
                    new Point(playheadX, 0),
                    new Point(playheadX, bounds.Height));
            }

            context.DrawLine(handlePen, new Point(startX, 0), new Point(startX, bounds.Height));
            context.DrawLine(handlePen, new Point(endX, 0), new Point(endX, bounds.Height));

            var startHandle = new Rect(
                Math.Clamp(startX - HandleBarWidth / 2, 0, Math.Max(0, bounds.Width - HandleBarWidth)),
                0,
                HandleBarWidth,
                bounds.Height);

            var endHandle = new Rect(
                Math.Clamp(endX - HandleBarWidth / 2, 0, Math.Max(0, bounds.Width - HandleBarWidth)),
                0,
                HandleBarWidth,
                bounds.Height);

            context.FillRectangle(handleBrush, startHandle);
            context.FillRectangle(handleBrush, endHandle);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (Bounds.Width <= 0 || Duration <= 0)
            {
                return;
            }

            var position = e.GetPosition(this);
            double time = TimeAt(position.X);
            double startX = TimeToX(SelectionStart);
            double endX = TimeToX(SelectionEnd);

            bool nearStart = Math.Abs(position.X - startX) <= HandleHitWidth;
            bool nearEnd = Math.Abs(position.X - endX) <= HandleHitWidth;

            if (nearStart && nearEnd)
            {
                _dragMode = Math.Abs(position.X - startX) <= Math.Abs(position.X - endX)
                    ? DragMode.Start
                    : DragMode.End;
            }
            else if (nearStart)
            {
                _dragMode = DragMode.Start;
            }
            else if (nearEnd)
            {
                _dragMode = DragMode.End;
            }
            else
            {
                _dragMode = DragMode.NewSelection;
                _anchorTime = time;
            }

            _pointerId = e.Pointer.Id;
            _pressPoint = position;
            _hasMoved = false;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (_dragMode == DragMode.None)
            {
                UpdateCursor(e.GetPosition(this));
                return;
            }

            if (e.Pointer.Id != _pointerId)
            {
                return;
            }

            var position = e.GetPosition(this);

            if (!_hasMoved)
            {
                if (Math.Abs(position.X - _pressPoint.X) < DragThresholdPixels)
                {
                    return;
                }

                _hasMoved = true;
            }

            double time = TimeAt(position.X);
            double minimum = Math.Max(0.01, MinimumSelectionSeconds);

            switch (_dragMode)
            {
                case DragMode.Start:
                    double start = Math.Clamp(time, 0, Math.Max(0, SelectionEnd - minimum));
                    UpdateSelection(start, SelectionEnd);
                    break;

                case DragMode.End:
                    double end = Math.Clamp(time, Math.Min(Duration, SelectionStart + minimum), Duration);
                    UpdateSelection(SelectionStart, end);
                    break;

                case DragMode.NewSelection:
                    double anchor = Math.Clamp(_anchorTime, 0, Duration);
                    double current = Math.Clamp(time, 0, Duration);
                    double left = Math.Min(anchor, current);
                    double right = Math.Max(anchor, current);

                    if (right - left < minimum)
                    {
                        if (current >= anchor)
                        {
                            right = Math.Min(Duration, left + minimum);
                        }
                        else
                        {
                            left = Math.Max(0, right - minimum);
                        }
                    }

                    UpdateSelection(left, right);
                    break;
            }

            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (_dragMode == DragMode.None || e.Pointer.Id != _pointerId)
            {
                return;
            }

            var position = e.GetPosition(this);

            if (_dragMode == DragMode.NewSelection && !_hasMoved)
            {
                // A simple click moves the playhead without touching the selection.
                PlayheadSeconds = TimeAt(position.X);
                InvalidateVisual();
            }

            _dragMode = DragMode.None;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);

            if (_dragMode == DragMode.None)
            {
                Cursor = new Cursor(StandardCursorType.Arrow);
            }
        }

        private void UpdateCursor(Point position)
        {
            if (Duration <= 0 || Bounds.Width <= 0)
            {
                Cursor = new Cursor(StandardCursorType.Arrow);
                return;
            }

            bool nearStart = Math.Abs(position.X - TimeToX(SelectionStart)) <= HandleHitWidth;
            bool nearEnd = Math.Abs(position.X - TimeToX(SelectionEnd)) <= HandleHitWidth;

            Cursor = new Cursor(nearStart || nearEnd
                ? StandardCursorType.SizeWestEast
                : StandardCursorType.Arrow);
        }

        private void UpdateSelection(double start, double end)
        {
            double duration = Duration;

            if (!double.IsFinite(duration) || duration <= 0)
            {
                return;
            }

            double clampedStart = Math.Clamp(start, 0, duration);
            double clampedEnd = Math.Clamp(end, 0, duration);

            SetCurrentValue(SelectionStartProperty, Math.Min(clampedStart, clampedEnd));
            SetCurrentValue(SelectionEndProperty, Math.Max(clampedStart, clampedEnd));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        private double TimeAt(double x)
        {
            if (Bounds.Width <= 0 || Duration <= 0)
            {
                return 0;
            }

            return Math.Clamp(x / Bounds.Width * Duration, 0, Duration);
        }

        private double TimeToX(double time)
        {
            if (Duration <= 0)
            {
                return 0;
            }

            return Math.Clamp(time, 0, Duration) / Duration * Bounds.Width;
        }
    }
}
