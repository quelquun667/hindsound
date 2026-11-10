using System.Diagnostics;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// The programs of the desktop the app starts (xdg-open, wl-copy, xclip, xsel): found on the PATH, started without a shell,
/// never waited on by the loop. What they answer later goes to the log.
/// </summary>
internal static class ExternalTools
{
    /// <summary>How long a program may take before the app stops waiting for its answer.</summary>
    public const int AnswerTimeoutMs = 5000;

    /// <summary>The full path of <paramref name="name"/> on <paramref name="pathVariable"/>, or null.</summary>
    public static string? Find(string name, string? pathVariable, Func<string, bool> isFile)
    {
        foreach (var folder in (pathVariable ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!folder.StartsWith('/'))
            {
                // A relative folder on the PATH would run whatever sits in the current folder.
                continue;
            }

            var candidate = folder.TrimEnd('/') + "/" + name;
            if (isFile(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary><see cref="Find(string, string?, Func{string, bool})"/> on the PATH of this process.</summary>
    public static string? Find(string name) => Find(name, Environment.GetEnvironmentVariable("PATH"), File.Exists);

    /// <summary>
    /// Starts <paramref name="program"/> with <paramref name="arguments"/>, writes <paramref name="input"/> to it when given,
    /// and returns at once. Null when it started; otherwise what went wrong. Its exit is watched off the caller's thread.
    /// </summary>
    public static string? Start(string program, IReadOnlyList<string> arguments, string? input, Action<string> log, string what)
    {
        var info = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"{what}: could not start ({ex.GetType().Name})";
        }

        if (process is null)
        {
            return $"{what}: could not start";
        }

        try
        {
            if (input is not null)
            {
                // A few hundred bytes: the pipe takes them without waiting for the program to read.
                process.StandardInput.Write(input);
                process.StandardInput.Close();
            }
        }
        catch (IOException ex)
        {
            process.Dispose();
            return $"{what}: the program took nothing ({ex.GetType().Name})";
        }

        _ = Task.Run(() => WatchExit(process, log, what));
        return null;
    }

    private static void WatchExit(Process process, Action<string> log, string what)
    {
        using (process)
        {
            try
            {
                if (!process.WaitForExit(AnswerTimeoutMs))
                {
                    // Some of them stay to serve what they hold (wl-copy, xclip); that is their way, not a failure.
                    return;
                }

                if (process.ExitCode != 0)
                {
                    log($"{what}: the program answered {process.ExitCode}");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                log($"{what}: no answer ({ex.GetType().Name})");
            }
        }
    }
}
