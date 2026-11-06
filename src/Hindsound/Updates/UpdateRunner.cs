using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Hindsound.Updates;

/// <summary>Inno Setup leaves its uninstaller next to the exe. Without it this is a development build, which never updates itself.</summary>
internal static class InstallKind
{
    public const string UninstallerName = "unins000.exe";

    public static bool UpdaterIsActive(string exeFolder) => File.Exists(Path.Combine(exeFolder, UninstallerName));
}

internal enum RunStatus
{
    Started,
    SetupMissing,

    /// <summary>The file on disk is no longer the one that was checked when it arrived.</summary>
    SetupChanged,

    /// <summary>The attempt could not be recorded, so nothing was started: an uncounted Setup could loop for ever.</summary>
    StateNotSaved,
    StartFailed,
}

/// <summary>
/// Starts the Setup waiting on disk. The caller quits right after <see cref="RunStatus.Started"/>; Setup waits for the
/// Mutex to go (/WAITEXIT), and restarts the app only with /RESTARTAPP.
/// </summary>
internal sealed class UpdateRunner(UpdateStateStore store, string folder, Action<ProcessStartInfo> start)
{
    /// <summary>/WAITEXIT outlasts the slowest exit of the app, 5 min for a clip being written and 5 s for the engine: an app leaving after Setup gave up would stay off.</summary>
    public static IReadOnlyList<string> SetupArguments(bool restartApp)
    {
        List<string> arguments = ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/WAITEXIT=330"];
        if (restartApp)
        {
            arguments.Add("/RESTARTAPP");
        }

        return arguments;
    }

    public static void StartProcess(ProcessStartInfo info) => Process.Start(info)?.Dispose();

    public RunStatus Run(bool restartApp)
    {
        var state = store.Load();
        if (state.Pending is not { } pending || !UpdateVersion.TryParse(pending.Version, out var version))
        {
            return RunStatus.SetupMissing;
        }

        var setup = UpdateFiles.SetupPath(folder, version);
        try
        {
            // Open from the check until the start: nobody can write to the file in between.
            using var file = new FileStream(setup, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!UpdateFiles.Matches(file, pending.Size, pending.Sha256))
            {
                // With the right size it would pass for downloaded for ever, and this version would never be fetched again.
                file.Dispose();
                UpdateFiles.TryDelete(setup);
                store.Save(state with { Pending = null });
                return RunStatus.SetupChanged;
            }

            if (!store.Save(state.WithAttempt(version)))
            {
                return RunStatus.StateNotSaved;
            }

            return TryStart(setup, restartApp, state, version);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return RunStatus.SetupMissing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return RunStatus.SetupChanged;
        }
    }

    private RunStatus TryStart(string setup, bool restartApp, UpdateStateData state, UpdateVersion version)
    {
        var info = new ProcessStartInfo(setup)
        {
            UseShellExecute = false,
            CreateNoWindow = true,

            // Not the install folder: a Setup must not hold the folder it is about to write to.
            WorkingDirectory = folder,
        };
        foreach (var argument in SetupArguments(restartApp))
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            start(info);
            return RunStatus.Started;
        }
        catch (Win32Exception)
        {
            // The attempt stays counted. Nothing is expected at the next launch, since nothing ran.
            store.Save(state.WithAttempt(version) with { ExpectedVersion = state.ExpectedVersion });
            return RunStatus.StartFailed;
        }
    }
}
