using System.Reflection;
using Hindsound.Export;
using Hindsound.Linux.Desktop;
using Hindsound.Ui;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// Every notification the app can raise, taken from the whole list of <c>NotificationStates</c>, asks for normal urgency and no
/// sound: GNOME and Plasma show no banner for a low one, so a notice that left the default would go unseen there.
/// </summary>
public sealed class EveryNotificationUrgencyTests
{
    private static readonly string[] ClipPaths = ["/home/u/Music/Hindsound/a.mp3"];

    private static ClipResult Clip(string[] paths, bool silent = false, bool rescued = false, bool wav = false) =>
        new(paths, TimeSpan.FromSeconds(30), 1000, silent, new float[24], rescued, wav);

    /// <summary>The notices that depend on a clip, with and without its file, in each of their shapes.</summary>
    private static IEnumerable<NotificationContent> ClipNotices()
    {
        foreach (var paths in new[] { ClipPaths, Array.Empty<string>() })
        {
            foreach (var (silent, rescued, wav) in new[] { (false, false, false), (true, false, false), (false, true, false), (false, false, true) })
            {
                foreach (var (wanted, copied) in new[] { (true, true), (true, false), (false, false) })
                {
                    yield return NotificationStates.ForClip(Clip(paths, silent, rescued, wav), TimeSpan.FromSeconds(30), wanted, copied, 0x44FF88);
                    yield return NotificationStates.ForClip(Clip(paths, silent, rescued, wav), TimeSpan.FromSeconds(60), wanted, copied, 0x44FF88);
                }
            }
        }
    }

    private static object? Sample(Type type)
    {
        var inner = Nullable.GetUnderlyingType(type) ?? type;
        if (inner == typeof(string))
        {
            return "sample";
        }

        if (inner == typeof(int))
        {
            return 45;
        }

        if (inner == typeof(uint))
        {
            return 0x44FF88u;
        }

        if (inner == typeof(bool))
        {
            return true;
        }

        if (inner == typeof(TimeSpan))
        {
            return TimeSpan.FromSeconds(30);
        }

        throw new NotSupportedException("The test cannot build an argument of type " + type + ": give it a sample.");
    }

    /// <summary>The notices with no clip: every public factory of the list but the one that takes a clip, with its arguments given and, when it has defaults, left out.</summary>
    private static IEnumerable<(string Name, NotificationContent Content)> OtherNotices()
    {
        var factories = typeof(NotificationStates)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(NotificationContent) && m.Name != nameof(NotificationStates.ForClip));
        foreach (var method in factories)
        {
            var parameters = method.GetParameters();
            yield return (method.Name, (NotificationContent)method.Invoke(null, parameters.Select(p => Sample(p.ParameterType)).ToArray())!);
            if (parameters.Any(p => p.HasDefaultValue))
            {
                yield return (method.Name + " with defaults", (NotificationContent)method.Invoke(null, parameters.Select(p => p.HasDefaultValue ? p.DefaultValue : Sample(p.ParameterType)).ToArray())!);
            }
        }
    }

    [Fact]
    public void The_list_is_walked_in_full()
    {
        // A guard against the walk going quiet: the list holds dozens of notices, and a clip comes in many shapes.
        Assert.True(OtherNotices().Count() >= 20);
        Assert.True(ClipNotices().Count() >= 40);
        Assert.Contains(ClipNotices(), c => c.ClipPath is not null);
        Assert.Contains(ClipNotices(), c => c.ClipPath is null);
    }

    [Fact]
    public void Every_notice_of_the_list_asks_for_normal_urgency_and_no_sound()
    {
        foreach (var (name, content) in OtherNotices())
        {
            var request = NotificationRequest.For(content, 0, markup: true);
            Assert.True(request.Urgency == 1, name + " asked for urgency " + request.Urgency);
            Assert.True(request.SuppressSound, name + " is not silent");
        }
    }

    [Fact]
    public void Every_notice_about_a_clip_asks_for_normal_urgency_and_no_sound()
    {
        foreach (var content in ClipNotices())
        {
            var request = NotificationRequest.For(content, 0, markup: true);
            Assert.True(request.Urgency == 1, content.Title + " asked for urgency " + request.Urgency);
            Assert.True(request.SuppressSound, content.Title + " is not silent");
        }
    }
}
