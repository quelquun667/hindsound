using System.Text;
using Hindsound.Linux.DBus;
using Hindsound.Ui;

namespace Hindsound.Linux.Desktop;

/// <summary>What a click on the notification asks for.</summary>
internal enum NotificationAction
{
    None,

    /// <summary>The notification itself was clicked.</summary>
    Click,

    /// <summary>The "Show in folder" button.</summary>
    ShowFolder,
}

/// <summary>
/// The arguments of org.freedesktop.Notifications.Notify for one notification of the app, in the order of the call:
/// app_name, replaces_id, app_icon, summary, body, actions, hints, expire_timeout (signature susssasa{sv}i).
/// </summary>
internal sealed record NotificationRequest(
    string AppName,
    uint ReplacesId,
    string Icon,
    string Summary,
    string Body,
    IReadOnlyList<string> Actions,
    byte Urgency,
    bool SuppressSound,
    string DesktopEntry,
    int ExpireTimeout)
{
    public const string Signature = "susssasa{sv}i";

    /// <summary>The key of the click on the notification itself, as the specification names it.</summary>
    public const string DefaultAction = "default";

    public const string ShowFolderAction = "show-folder";

    public const string ShowFolderLabel = "Show in folder";

    /// <summary>Most servers never show the label of the default action; some read it out.</summary>
    public const string DefaultLabel = "Open";

    /// <summary>
    /// The urgency of every notification of the app (the levels of the specification are 0 low, 1 normal, 2 critical). GNOME and
    /// Plasma show a low one only in their list, with no banner, so a failed save, a lost microphone or a new version would go
    /// unseen; Windows shows all of them, and so does this.
    /// </summary>
    public const byte NormalUrgency = 1;

    /// <summary>-1: the server's own settings decide how long it stays.</summary>
    public const int ServerTimeout = -1;

    public const string AppNameText = "Hindsound";

    /// <summary>
    /// The request for <paramref name="content"/>. It replaces <paramref name="replacesId"/> in place (0 for none): one
    /// notification at a time, as on Windows. <paramref name="markup"/> is whether the server reads markup in the body, which
    /// then needs its &amp;, &lt; and &gt; escaped: a file name may hold them.
    /// </summary>
    public static NotificationRequest For(NotificationContent content, uint replacesId, bool markup)
    {
        var actions = new List<string> { DefaultAction, DefaultLabel };
        if (content.ClipPath is not null)
        {
            actions.Add(ShowFolderAction);
            actions.Add(ShowFolderLabel);
        }

        var body = BodyOf(content);
        return new NotificationRequest(
            AppNameText,
            replacesId,
            DBusNames.AppId,
            content.Title,
            markup ? Escape(body) : body,
            actions,
            NormalUrgency,
            SuppressSound: true,
            DBusNames.AppId,
            ServerTimeout);
    }

    /// <summary>The text, with the keys the Windows notification draws as key caps written after it: "Paste Ctrl+V".</summary>
    public static string BodyOf(NotificationContent content) =>
        content.Keys.Count == 0 ? content.Body : content.Body + " " + string.Join("+", content.Keys);

    /// <summary>The markup of the specification is a small subset of XML: these three characters are all it needs escaped.</summary>
    public static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            escaped.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                _ => c.ToString(),
            });
        }

        return escaped.ToString();
    }

    /// <summary>What an ActionInvoked key asks for.</summary>
    public static NotificationAction ActionOf(string? key) => key switch
    {
        DefaultAction => NotificationAction.Click,
        ShowFolderAction => NotificationAction.ShowFolder,
        _ => NotificationAction.None,
    };
}
