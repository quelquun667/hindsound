using Hindsound.App;
using Hindsound.Ui;
using Hindsound.Ui.Window;
using Hindsound.Unix.Notification;

namespace Hindsound.Linux.Tests.Notification;

/// <summary>A text whose every letter is half an em wide and that only writes down what it is asked to draw.</summary>
internal sealed class FakeText : ICanvasText
{
    public List<(Face Face, float Size, string Text, float X, float Top, uint Rgb)> Drawn { get; } = [];

    public bool Disposed { get; private set; }

    public float Measure(FontSpec font, string text) => text.Length * MathF.Floor(font.Size + 0.5f) / 2;

    public float LineHeight(FontSpec font) => MathF.Round(MathF.Floor(font.Size + 0.5f) * 1.33f);

    public void Draw(Pixels target, FontSpec font, string text, float x, float top, uint rgb)
    {
        if (text.Length > 0)
        {
            Drawn.Add((font.Face, font.Size, text, x, top, rgb));
        }
    }

    public void Dispose() => Disposed = true;
}

/// <summary>A loop that runs nothing by itself: the test moves its clock and runs what is due.</summary>
internal sealed class FakeLoop : IAppLoop
{
    private readonly List<(long At, Action Action)> _timers = [];

    public long Now { get; set; }

    public List<int> Delays { get; } = [];

    public Action? CloseAsked { get; set; }

    public Action? ScaleMayHaveChanged { get; set; }

    public int Pending => _timers.Count;

    public void Post(Action action) => _timers.Add((Now, action));

    public void After(int milliseconds, Action action)
    {
        Delays.Add(milliseconds);
        _timers.Add((Now + milliseconds, action));
    }

    /// <summary>Moves the clock by steps of 1 ms up to <paramref name="milliseconds"/> from now, running each timer when due.</summary>
    public void Advance(int milliseconds)
    {
        var end = Now + milliseconds;
        while (true)
        {
            var due = _timers.Where(t => t.At <= end).OrderBy(t => t.At).FirstOrDefault();
            if (due.Action is null)
            {
                break;
            }

            _timers.Remove(due);
            Now = Math.Max(Now, due.At);
            due.Action();
        }

        Now = end;
    }

    public void Run() => throw new NotSupportedException();

    public void Quit()
    {
    }

    public bool InFront() => false;

    public void Dispose()
    {
    }
}

/// <summary>A window that keeps what it was asked.</summary>
internal sealed class FakeSurface : INotificationSurface
{
    public ScreenRect Default { get; set; } = new(0, 25, 1440, 900);

    public double Density { get; set; } = 1;

    public List<(ScreenRect Bounds, bool ClickThrough)> Opened { get; } = [];

    public List<(bool Changed, (int SourceX, int DestinationX, int Width) Slice, uint Corner)> Shown { get; } = [];

    public int PendingClicks { get; set; }

    public int Closed { get; private set; }

    public bool Disposed { get; private set; }

    public ScreenRect DefaultArea() => Default;

    public double Open(ScreenRect bounds, bool clickThrough)
    {
        Opened.Add((bounds, clickThrough));
        return Density;
    }

    public void Show(NotificationPicture picture, bool pictureChanged, (int SourceX, int DestinationX, int Width) slice) =>
        Shown.Add((pictureChanged, slice, picture.PixelAt(picture.Width - 1, 0)));

    public int TakeClicks()
    {
        var clicks = PendingClicks;
        PendingClicks = 0;
        return clicks;
    }

    public void Close() => Closed++;

    public void Dispose() => Disposed = true;
}
