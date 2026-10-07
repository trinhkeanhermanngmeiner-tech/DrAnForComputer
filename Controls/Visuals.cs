using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace BacSiMayTinh.Controls
{
    /// <summary>Vòng tròn hiển thị điểm sức khỏe (vẽ trực tiếp, rất nhẹ).</summary>
    public class RingGauge : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(RingGauge),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(RingGauge),
            new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(RingGauge),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(nameof(ValueBrush), typeof(Brush), typeof(RingGauge),
            new FrameworkPropertyMetadata(Brushes.Green, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
        public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
        public Brush ValueBrush { get => (Brush)GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }

        private const double Start = 135, Sweep = 270;

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return;
            double r = (size - Thickness) / 2;
            var c = new Point(ActualWidth / 2, ActualHeight / 2);
            var track = new Pen(TrackBrush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, track, Arc(c, r, Start, Sweep));
            double v = Math.Max(0, Math.Min(100, Value));
            if (v > 0.5)
            {
                var pen = new Pen(ValueBrush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawGeometry(null, pen, Arc(c, r, Start, Sweep * v / 100));
            }
        }

        private static Geometry Arc(Point c, double r, double startDeg, double sweepDeg)
        {
            Point P(double deg) { var a = deg * Math.PI / 180; return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)); }
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(P(startDeg), false, false);
                ctx.ArcTo(P(startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }
    }

    /// <summary>Biểu đồ đường mini cho CPU/RAM theo thời gian thực.</summary>
    public class Sparkline : FrameworkElement
    {
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Sparkline),
            new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

        public double Max { get; set; } = 100;
        public int Capacity { get; set; } = 60;
        private readonly List<double> _values = new List<double>();

        public void Push(double v)
        {
            _values.Add(v);
            if (_values.Count > Capacity) _values.RemoveAt(0);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (_values.Count < 2 || ActualWidth <= 0) return;
            double max = Max;
            if (max <= 0) max = Math.Max(1, _values.Max());
            double step = ActualWidth / (Capacity - 1);
            double x0 = ActualWidth - step * (_values.Count - 1);
            var line = new StreamGeometry();
            var area = new StreamGeometry();
            Point Pt(int i) => new Point(x0 + i * step, ActualHeight - Math.Min(1, _values[i] / max) * (ActualHeight - 2) - 1);
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(Pt(0), false, false);
                for (int i = 1; i < _values.Count; i++) ctx.LineTo(Pt(i), true, true);
            }
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(x0, ActualHeight), true, true);
                for (int i = 0; i < _values.Count; i++) ctx.LineTo(Pt(i), false, false);
                ctx.LineTo(new Point(ActualWidth, ActualHeight), false, false);
            }
            var fill = Stroke.Clone();
            fill.Opacity = 0.15;
            dc.DrawGeometry(fill, null, area);
            dc.DrawGeometry(null, new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round }, line);
        }
    }

    /// <summary>Vòng xoay chờ – chỉ chạy hoạt ảnh khi đang hiển thị để tiết kiệm CPU.</summary>
    public class Spinner : Grid
    {
        private readonly RotateTransform _rot = new RotateTransform();
        private readonly DoubleAnimation _anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever };

        public Spinner()
        {
            Width = Height = 36;
            var arc = new System.Windows.Shapes.Path
            {
                StrokeThickness = 4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = Geometry.Parse("M 18,3 A 15,15 0 1 1 3,18"),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _rot,
                Width = 36,
                Height = 36,
            };
            arc.SetResourceReference(Shape.StrokeProperty, "B.Accent");
            Children.Add(arc);
            IsVisibleChanged += (s, e) => Update();
            Loaded += (s, e) => Update();
            Unloaded += (s, e) => _rot.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        private void Update()
        {
            if (IsVisible) _rot.BeginAnimation(RotateTransform.AngleProperty, _anim);
            else _rot.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }

    /// <summary>Màu cố định cho nhãn trạng thái (dùng được ở cả giao diện sáng và tối).</summary>
    public static class Pal
    {
        public static readonly SolidColorBrush Green = Make("#22B573"), GreenBg = Make("#2622B573");
        public static readonly SolidColorBrush Amber = Make("#E8A317"), AmberBg = Make("#26E8A317");
        public static readonly SolidColorBrush Red = Make("#EF4B4B"), RedBg = Make("#26EF4B4B");
        public static readonly SolidColorBrush Blue = Make("#3B9EFF"), BlueBg = Make("#263B9EFF");
        public static readonly SolidColorBrush Gray = Make("#8A99AB"), GrayBg = Make("#268A99AB");
        public static readonly SolidColorBrush Purple = Make("#A78BFA"), PurpleBg = Make("#26A78BFA");

        public static SolidColorBrush Make(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public static Brush RiskFg(Core.Risk r) => r == Core.Risk.Safe ? Green : r == Core.Risk.Moderate ? Amber : Red;
        public static Brush RiskBg(Core.Risk r) => r == Core.Risk.Safe ? GreenBg : r == Core.Risk.Moderate ? AmberBg : RedBg;
    }
}
