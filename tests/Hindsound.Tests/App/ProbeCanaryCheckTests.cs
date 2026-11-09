using Hindsound.App;

namespace Hindsound.Tests.App;

/// <summary>The probe's check that a collection's name never reached the log, which must not trip on a path the probe was given.</summary>
public sealed class ProbeCanaryCheckTests
{
    private const string Canary = "Collection-Canary-7F3";

    [Fact]
    public void A_log_with_no_canary_is_clean()
    {
        Assert.False(HindsoundApp.LogNamesTheCanary(["started, settings: C:\\Temp\\probe.json (Loaded), 12 MB", "library read: failed (IOException)"]));
    }

    [Fact]
    public void A_canary_in_any_other_line_is_found_whatever_its_case()
    {
        Assert.True(HindsoundApp.LogNamesTheCanary(["started, settings: C:\\Temp\\probe.json (Loaded)", "library action: failed " + Canary.ToUpperInvariant()]));
    }

    [Fact]
    public void The_path_of_the_settings_the_probe_was_given_may_hold_the_canary()
    {
        var path = $@"C:\Temp\{Canary}\{Canary}.json";

        Assert.False(HindsoundApp.LogNamesTheCanary([$"started, settings: {path} (Loaded), 12 MB"]));
    }

    [Fact]
    public void A_line_that_is_not_the_first_never_gets_that_leave()
    {
        Assert.True(HindsoundApp.LogNamesTheCanary(["started, settings: C:\\Temp\\probe.json (Loaded)", $"settings: {Canary}"]));
    }
}
