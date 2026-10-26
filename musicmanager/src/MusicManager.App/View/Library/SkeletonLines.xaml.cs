using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI;

namespace MusicManager.App.View.Library;

/// <summary>
/// What a list will look like, drawn as "redacted" blocks while it is being worked out: slightly rounded bars, each row a
/// time stub and two lines, like the changed lyric lines of the song window. When the control is given a Height the number
/// of bars is the number that fit it, otherwise it is RowCount rows of two; OneBar makes a single short bar.
///
/// All the bars show ONE gradient (the blue-violet-pink of the status word, as a soft tint) that runs at 45 degrees across
/// the whole block, so the colours line up from bar to bar, and that flows: the colours move along it, easing in and out
/// (slow, fast, slow). It runs while the control is in the tree and visible.
/// </summary>
public sealed partial class SkeletonLines : UserControl
{
    private const double BarHeight = 12;
    private const double LineGap = 8; // between the two lines of a row
    private const double RowGap = 16; // between the rows
    private const double StubWidth = 34;
    private const double StubGap = 14;
    private const double Corner = 3; // a small radius: soft corners, not pills

    private static readonly Color[] Palette =
    {
        Color.FromArgb(255, 0x4F, 0x8D, 0xFF), // blue
        Color.FromArgb(255, 0x9B, 0x6B, 0xFF), // violet
        Color.FromArgb(255, 0xFF, 0x6B, 0xC8), // pink
    };

    private static readonly double[] Fractions = { 0.88, 0.70, 0.76, 0.92, 0.64, 0.80, 0.84, 0.58 };

    private sealed class Bar
    {
        public Border Element;
        public double Left, Top;
        public LinearGradientBrush Brush;
    }

    private readonly List<Bar> _bars = new();
    private Storyboard _flow;

    /// <summary>how many rows (a time stub and two bars each) when the control has no Height of its own</summary>
    public int RowCount { get; set; } = 3;

    /// <summary>a single short bar, for a one-line value</summary>
    public bool OneBar { get; set; }

    public SkeletonLines()
    {
        InitializeComponent();
        Loaded += (_, _) => Rebuild();
        Unloaded += (_, _) => _flow?.Stop();
        SizeChanged += (_, e) =>
        {
            if (IsLoaded && (int)e.PreviousSize.Width != (int)e.NewSize.Width)
            {
                Rebuild();
            }
        };
        RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Visible && IsLoaded)
            {
                StartFlow();
            }
            else
            {
                _flow?.Stop();
            }
        });
        ActualThemeChanged += (_, _) => StartFlow();
    }

    // ------------------------------------------------------------------ layout

    private void Rebuild()
    {
        Surface.Children.Clear();
        _bars.Clear();
        double width = ActualWidth;
        if (width < 80)
        {
            return;
        }
        double lineLeft = OneBar ? 0 : StubWidth + StubGap;

        // how many bars: as many as fit the Height when there is one, otherwise the rows asked for
        int count;
        if (OneBar)
        {
            count = 1;
        }
        else if (!double.IsNaN(Height) && Height > 0)
        {
            count = 0;
            while (BarBottom(count) <= Height)
            {
                count++;
            }
            count = Math.Max(1, count);
        }
        else
        {
            count = Math.Max(1, RowCount * 2);
        }

        Surface.Width = width;
        Surface.Height = BarBottom(count - 1);
        for (int i = 0; i < count; i++)
        {
            double lineWidth = Math.Max(24, (width - lineLeft) * (OneBar ? 0.55 : Fractions[i % Fractions.Length]));
            AddBar(lineLeft, BarTop(i), lineWidth, BarHeight);
            if (!OneBar && i % 2 == 0)
            {
                AddBar(0, BarTop(i) + 1, StubWidth, 10); // the time stub of the row
            }
        }
        AlignBrushes();
        StartFlow();
    }

    /// <summary>the top of bar i: two lines to a row, a bigger gap between the rows</summary>
    private static double BarTop(int i) => i / 2 * (2 * BarHeight + LineGap + RowGap) + i % 2 * (BarHeight + LineGap);

    private static double BarBottom(int i) => BarTop(i) + BarHeight;

    private void AddBar(double left, double top, double width, double height)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(300, 300),
            Transform = new TranslateTransform(),
        };
        brush.GradientStops.Add(new GradientStop { Offset = 0, Color = Tint(Palette[0]) });
        brush.GradientStops.Add(new GradientStop { Offset = 0.5, Color = Tint(Palette[1]) });
        brush.GradientStops.Add(new GradientStop { Offset = 1, Color = Tint(Palette[2]) });
        var element = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(Corner),
            Background = brush,
        };
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        Surface.Children.Add(element);
        _bars.Add(new Bar { Element = element, Left = left, Top = top, Brush = brush });
    }

    /// <summary>every bar draws the same gradient laid over the whole block: a line as long in x as in y (45 degrees) from the
    /// top left to the bottom right corner, moved so that it starts at the block's corner, not at the bar's own</summary>
    private void AlignBrushes()
    {
        double reach = Math.Max(60, (Surface.Width + Surface.Height) / 2);
        foreach (Bar bar in _bars)
        {
            bar.Brush.EndPoint = new Point(reach, reach);
            var shift = (TranslateTransform)bar.Brush.Transform;
            shift.X = -bar.Left;
            shift.Y = -bar.Top;
        }
    }

    // ------------------------------------------------------------------ the flowing gradient

    /// <summary>a soft tint of a palette colour (more opaque on a dark card, where it has to carry more)</summary>
    private Color Tint(Color color)
    {
        byte alpha = ActualTheme == ElementTheme.Dark ? (byte)0x80 : (byte)0x6A;
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    /// <summary>each stop takes the next stop's colour in turn (6 s for a full turn), easing in and out of every step</summary>
    private void StartFlow()
    {
        _flow?.Stop();
        if (_bars.Count == 0 || Visibility != Visibility.Visible)
        {
            return;
        }
        _flow = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        foreach (Bar bar in _bars)
        {
            for (int stopIndex = 0; stopIndex < 3; stopIndex++)
            {
                var colors = new ColorAnimationUsingKeyFrames { EnableDependentAnimation = true };
                for (int step = 0; step <= 3; step++)
                {
                    colors.KeyFrames.Add(new EasingColorKeyFrame
                    {
                        KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(step * 2)),
                        Value = Tint(Palette[(stopIndex + step) % Palette.Length]),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                    });
                }
                Storyboard.SetTarget(colors, bar.Brush.GradientStops[stopIndex]);
                Storyboard.SetTargetProperty(colors, "Color");
                _flow.Children.Add(colors);
            }
        }
        _flow.Begin();
    }
}
