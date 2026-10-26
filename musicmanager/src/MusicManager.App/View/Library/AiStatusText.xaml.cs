using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace MusicManager.App.View.Library;

/// <summary>
/// The animated status line of a step that takes a moment (Claude, checking, analyzing; it does not say what is being
/// asked): it cycles through a few words (Identifying, Classifying, Analyzing, Thinking) next to a spinning sparkle star, in
/// a blue-violet-pink gradient (at 45 degrees) that flows with a slow-fast-slow ease. The next word replaces the old one at
/// once. It runs while it is in the tree and visible, and stops when it is removed or collapsed. The animations are built in
/// code and attached to the objects themselves (gradient stops and transforms live inside brushes, which a Storyboard cannot
/// find by name).
/// </summary>
public sealed partial class AiStatusText : UserControl
{
    private static readonly string[] Words = { "Identifying…", "Classifying…", "Analyzing…", "Thinking…" };
    private static readonly Color[] Palette =
    {
        Color.FromArgb(255, 0x4F, 0x8D, 0xFF), // blue
        Color.FromArgb(255, 0x9B, 0x6B, 0xFF), // violet
        Color.FromArgb(255, 0xFF, 0x6B, 0xC8), // pink
    };

    private readonly DispatcherTimer _wordTimer = new() { Interval = TimeSpan.FromMilliseconds(2400) };
    private readonly List<Storyboard> _running = new();
    private int _index;

    public AiStatusText()
    {
        InitializeComponent();
        WordBase.SizeChanged += (_, _) => SetDiagonal((LinearGradientBrush)WordBase.Foreground, WordBase.ActualWidth, WordBase.ActualHeight);
        Loaded += (_, _) => StartIfVisible();
        Unloaded += (_, _) => Stop();
        RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => StartIfVisible());
        _wordTimer.Tick += (_, _) => ChangeWord();
    }

    private void StartIfVisible()
    {
        if (Visibility == Visibility.Visible && IsLoaded)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    /// <summary>starts the star, the colour flow, the highlight and the word cycle</summary>
    public void Start()
    {
        Stop();
        WordBase.Text = Words[_index];
        SetDiagonal((LinearGradientBrush)WordBase.Foreground, WordBase.ActualWidth, WordBase.ActualHeight);

        // the text and the star take the colours of the palette in turn: each stop moves on to the next colour every 2 s
        var wordBrush = (LinearGradientBrush)WordBase.Foreground;
        var starBrush = (LinearGradientBrush)BigStar.Fill;
        var flow = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        AddFlow(flow, wordBrush.GradientStops[0], 0);
        AddFlow(flow, wordBrush.GradientStops[1], 1);
        AddFlow(flow, wordBrush.GradientStops[2], 2);
        AddFlow(flow, starBrush.GradientStops[0], 0);
        AddFlow(flow, starBrush.GradientStops[1], 2);
        Run(flow);

        // the star: a steady spin, a breathing pulse, and a small twinkle star that turns the other way
        var bigStar = (CompositeTransform)BigStar.RenderTransform;
        var smallStar = (CompositeTransform)SmallStar.RenderTransform;
        var spin = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        spin.Children.Add(Anim(bigStar, "Rotation", 0, 360, 2.4, false, true));
        spin.Children.Add(Anim(bigStar, "ScaleX", 0.82, 1.08, 1.1, true, true));
        spin.Children.Add(Anim(bigStar, "ScaleY", 0.82, 1.08, 1.1, true, true));
        spin.Children.Add(Anim(smallStar, "Rotation", 0, -360, 1.8, false, true));
        var twinkle = new DoubleAnimationUsingKeyFrames { EnableDependentAnimation = true };
        twinkle.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0.25 });
        twinkle.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5)), Value = 1 });
        twinkle.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.0)), Value = 0.25 });
        Storyboard.SetTarget(twinkle, SmallStar);
        Storyboard.SetTargetProperty(twinkle, "Opacity");
        spin.Children.Add(twinkle);
        Run(spin);

        _wordTimer.Start();
    }

    /// <summary>stops everything (the step is done)</summary>
    public void Stop()
    {
        _wordTimer.Stop();
        foreach (var storyboard in _running)
        {
            storyboard.Stop();
        }
        _running.Clear();
    }

    // ------------------------------------------------------------------ the word change

    /// <summary>the next word replaces the old one at once (a hard cut, no blur and no fade)</summary>
    public void ChangeWord()
    {
        _index = (_index + 1) % Words.Length;
        WordBase.Text = Words[_index];
    }

    /// <summary>a gradient line as long in x as in y runs at exactly 45 degrees; this one is long enough to take the colours
    /// from one corner of the box to the opposite one</summary>
    private static void SetDiagonal(LinearGradientBrush brush, double width, double height)
    {
        double reach = Math.Max(1, (width + height) / 2);
        brush.StartPoint = new Windows.Foundation.Point(0, 0);
        brush.EndPoint = new Windows.Foundation.Point(reach, reach);
    }

    // ------------------------------------------------------------------ helpers

    private void Run(Storyboard storyboard)
    {
        _running.Add(storyboard);
        storyboard.Begin();
    }

    /// <summary>one gradient stop goes through the palette, starting at the colour with this phase (a full turn takes 6 s)</summary>
    private static void AddFlow(Storyboard storyboard, GradientStop stop, int phase)
    {
        var colors = new ColorAnimationUsingKeyFrames { EnableDependentAnimation = true };
        for (int step = 0; step <= 3; step++)
        {
            colors.KeyFrames.Add(new EasingColorKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(step * 2)),
                Value = Palette[(phase + step) % Palette.Length],
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            });
        }
        Storyboard.SetTarget(colors, stop);
        Storyboard.SetTargetProperty(colors, "Color");
        storyboard.Children.Add(colors);
    }

    private static DoubleAnimation Anim(DependencyObject target, string property, double from, double to, double seconds,
            bool autoReverse, bool sine)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromSeconds(seconds)),
            AutoReverse = autoReverse,
            EnableDependentAnimation = true,
        };
        if (sine)
        {
            animation.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
        }
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }
}
