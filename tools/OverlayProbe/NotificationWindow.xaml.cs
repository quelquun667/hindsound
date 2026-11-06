using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using static OverlayProbe.NativeMethods;

namespace OverlayProbe;

public partial class NotificationWindow : Window
{
    private static readonly double[] BarValues =
    [
        0.25, 0.25, 0.3, 0.39, 0.42, 0.4, 0.15, 0.12, 0.38, 0.44, 0.51, 0.26,
        0.14, 0.23, 0.35, 0.44, 0.36, 0.35, 0.78, 0.84, 0.88, 0.59, 0.8, 0.8,
    ];

    private readonly bool _softwareOnly;
    private IntPtr _handle;
    private Storyboard? _running;

    public event Action? Finished;

    public NotificationWindow(bool softwareOnly)
    {
        _softwareOnly = softwareOnly;
        InitializeComponent();
        DrawBars();
        SourceInitialized += OnSourceInitialized;
        _handle = new WindowInteropHelper(this).EnsureHandle();
    }

    private void DrawBars()
    {
        var accent = new SolidColorBrush(Color.FromRgb(0xD4, 0xF5, 0x3C));
        accent.Freeze();
        for (var i = 0; i < BarValues.Length; i++)
        {
            var height = Math.Max(2, 2 * Math.Round(BarValues[i] * 20));
            var bar = new Rectangle { Width = 2, Height = height, Fill = accent, RadiusX = 1, RadiusY = 1 };
            Canvas.SetLeft(bar, 1 + i * 4);
            Canvas.SetTop(bar, (40 - height) / 2);
            Bars.Children.Add(bar);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_handle)!;
        source.AddHook(WndProc);
        if (_softwareOnly)
        {
            source.CompositionTarget!.RenderMode = RenderMode.SoftwareOnly;
        }

        var style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(_handle, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow));
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmMouseActivate)
        {
            handled = true;
            return new IntPtr(MaNoActivate);
        }

        return IntPtr.Zero;
    }

    /// Shows the notification on the given area, in physical pixels, without ever activating the window.
    internal void Present(NativeMethods.Rect area, uint dpi, bool clickThrough)
    {
        _running?.Stop(this);

        var style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        style = clickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr(_handle, GwlExStyle, new IntPtr(style));

        var scale = dpi / 96.0;
        var width = (int)Math.Round(360 * scale);
        var height = (int)Math.Round(64 * scale);
        var x = area.Right - width;
        var y = area.Top + (int)Math.Round(area.Height * 0.30);
        SetWindowPos(_handle, HwndTopmost, x, y, width, height, SwpNoActivate);
        if (!IsVisible)
        {
            Show();
        }

        SetWindowPos(_handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        Animate();
    }

    private void Animate()
    {
        var storyboard = new Storyboard();
        Timeline.SetDesiredFrameRate(storyboard, 60);

        var slideIn = new DoubleAnimation(360, 0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(slideIn, Root);
        Storyboard.SetTargetProperty(slideIn, new PropertyPath("RenderTransform.X"));
        storyboard.Children.Add(slideIn);

        var slideOut = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = TimeSpan.FromMilliseconds(140 + 2600),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(slideOut, Root);
        Storyboard.SetTargetProperty(slideOut, new PropertyPath("RenderTransform.X"));
        storyboard.Children.Add(slideOut);

        var rise = new DoubleAnimation(0.05, 1, TimeSpan.FromMilliseconds(200));
        Storyboard.SetTarget(rise, Bars);
        Storyboard.SetTargetProperty(rise, new PropertyPath("RenderTransform.ScaleY"));
        storyboard.Children.Add(rise);

        storyboard.Completed += (_, _) =>
        {
            Hide();
            Finished?.Invoke();
        };
        _running = storyboard;
        storyboard.Begin(this, isControllable: true);
    }
}
