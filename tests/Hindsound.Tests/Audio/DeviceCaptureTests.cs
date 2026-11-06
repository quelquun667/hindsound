using System.Diagnostics;
using Hindsound.Audio;
using NAudio.CoreAudioApi;
using Xunit.Abstractions;

namespace Hindsound.Tests.Audio;

/// <summary>Opens the real loopback of this PC for a few seconds, in memory only. Never the microphone, and plays nothing.</summary>
[Trait("Category", "Device")]
[Collection(nameof(TimingSensitive))]
public class DeviceCaptureTests(ITestOutputHelper output)
{
    private static long NowTicks()
    {
        var counter = Stopwatch.GetTimestamp();
        return counter / Stopwatch.Frequency * 10_000_000 + counter % Stopwatch.Frequency * 10_000_000 / Stopwatch.Frequency;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Packet_stamps_are_on_the_performance_counter(bool excludeOwnProcess)
    {
        var factory = new WasapiCaptureSourceFactory();
        var device = factory.ResolveDevice(AudioFlow.System, null);
        Assert.NotNull(device);

        using var source = await factory.OpenAsync(new CaptureRequest(AudioFlow.System, device!, excludeOwnProcess));
        var lock_ = new object();
        var stamps = new List<(long Stamp, long Now, int Bytes, AudioClientBufferFlags Flags)>();
        source.PacketAvailable += (ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long position, long qpc) =>
        {
            var now = NowTicks();
            lock (lock_)
            {
                stamps.Add((qpc, now, data.Length, flags));
            }
        };

        source.Start();
        await Task.Delay(3000);
        source.Stop();

        output.WriteLine($"Format: {source.Format}, device: {source.DeviceName}");
        output.WriteLine($"Packets: {stamps.Count}");

        // The format the device really reports must be one the converter can read.
        Assert.NotNull(new AudioFormatConverter(source.Format, InternalFormat.SystemChannels));

        lock (lock_)
        {
            var empty = stamps.Count(entry => entry.Bytes == 0);
            var unstamped = stamps.Count(entry => entry.Bytes > 0 && entry.Stamp <= 0);
            output.WriteLine($"Empty packets: {empty}, holding audio without a stamp: {unstamped}");
            Assert.Equal(0, unstamped);

            var valid = stamps.Where(entry => entry.Stamp > 0).ToList();
            output.WriteLine($"With a stamp: {valid.Count}");
            if (valid.Count == 0)
            {
                // Nothing played and the classic loopback sends nothing then. That proves nothing about the stamps.
                Assert.False(excludeOwnProcess && stamps.Count > 0, "Process loopback packets carry no stamp");
                return;
            }

            var ages = valid.Select(entry => (entry.Now - entry.Stamp) / 10_000.0).ToList();
            output.WriteLine($"Stamp age: {ages.Min():F1} to {ages.Max():F1} ms");
            var frameBytes = source.Format.BlockAlign;
            var jumps = new List<double>();
            for (var i = 1; i < valid.Count; i++)
            {
                var expected = valid[i - 1].Bytes / frameBytes * 10_000_000.0 / source.Format.SampleRate;
                jumps.Add((valid[i].Stamp - valid[i - 1].Stamp - expected) / 10_000.0);
            }

            if (jumps.Count > 0)
            {
                output.WriteLine($"Gap between consecutive packets, beyond their length: {jumps.Min():F2} to {jumps.Max():F2} ms");
            }

            Assert.All(ages, age => Assert.InRange(age, -5, 2000));
        }
    }

    [Fact]
    public async Task The_engine_captures_the_real_loopback_into_memory()
    {
        using var engine = new AudioEngine(new AudioEngineOptions(TimeSpan.FromSeconds(30), MicEnabled: false));

        await engine.StartAsync();
        var status = engine.Status;
        output.WriteLine($"{status}");
        Assert.Equal(CaptureActivity.Capturing, status.Activity);
        Assert.True(status.OwnSoundsExcluded);
        Assert.False(status.MicCapturing);

        await Task.Delay(3000);
        var snapshot = engine.Snapshot(TimeSpan.FromSeconds(2));
        output.WriteLine($"Snapshot: {snapshot.Duration}, peak {snapshot.System.Max(Math.Abs):F3}");

        Assert.InRange(snapshot.Duration.TotalSeconds, 1.9, 2.0);
        Assert.Empty(snapshot.Mic);

        await engine.PauseAsync();
        var kept = engine.Snapshot(TimeSpan.FromSeconds(2));
        Assert.Equal(CaptureActivity.Paused, engine.Status.Activity);
        Assert.InRange(kept.Duration.TotalSeconds, 1.9, 2.0);

        await engine.ResumeAsync();
        Assert.Equal(CaptureActivity.Capturing, engine.Status.Activity);
        await Task.Delay(500);
        await engine.StopAsync();
        Assert.Equal(CaptureActivity.Stopped, engine.Status.Activity);
    }

    [Fact]
    public void Devices_can_be_listed_and_resolved()
    {
        var outputs = WasapiCaptureSourceFactory.ListDevices(AudioFlow.System);
        output.WriteLine(string.Join(", ", outputs.Select(device => device.Name)));

        var factory = new WasapiCaptureSourceFactory();
        Assert.NotEmpty(outputs);
        Assert.Equal(outputs[0].Id, factory.ResolveDevice(AudioFlow.System, outputs[0].Id));
        Assert.Null(factory.ResolveDevice(AudioFlow.System, "{not-a-device}"));
    }
}
