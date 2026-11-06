using System.Runtime.InteropServices;
using Hindsound.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Hindsound.Tests.Audio;

internal sealed class FakeClock
{
    public long Ticks { get; set; } = 1_000_000_000;

    public void Advance(double seconds) => Ticks += (long)Math.Round(seconds * 10_000_000);
}

internal sealed class FakeSource(AudioFlow flow, CaptureRequest request, string name) : ICaptureSource
{
    public AudioFlow Flow { get; } = flow;

    public CaptureRequest Request { get; } = request;

    public WaveFormat Format { get; } = flow == AudioFlow.System
        ? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)
        : WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);

    public string? DeviceName { get; } = name;

    public bool Started { get; private set; }

    public bool Disposed { get; private set; }

    public event CaptureDataAvailableHandler? PacketAvailable;

    public event Action<Exception?>? Stopped;

    public void Start() => Started = true;

    public void Stop() => Stopped?.Invoke(null);

    public void Dispose() => Disposed = true;

    public void Deliver(ReadOnlySpan<float> samples, long qpc, AudioClientBufferFlags flags = AudioClientBufferFlags.None)
    {
        PacketAvailable?.Invoke(MemoryMarshal.AsBytes(samples), flags, 0, qpc);
    }

    public void Die(Exception exception) => Stopped?.Invoke(exception);
}

internal sealed class FakeFactory : ICaptureSourceFactory
{
    public string? SystemDevice { get; set; } = "speakers";

    public string? MicDevice { get; set; } = "headset-mic";

    public bool FailExclusion { get; set; }

    public bool FailEverything { get; set; }

    public List<FakeSource> Opened { get; } = [];

    public FakeSource? Latest(AudioFlow flow) => Opened.LastOrDefault(source => source.Flow == flow);

    public string? ResolveDevice(AudioFlow flow, string? requestedId)
    {
        var current = flow == AudioFlow.System ? SystemDevice : MicDevice;
        return requestedId is null || requestedId == current ? current : null;
    }

    public Task<ICaptureSource> OpenAsync(CaptureRequest request)
    {
        if (FailEverything || (FailExclusion && request.ExcludeOwnProcess))
        {
            throw new COMException("Activation failed");
        }

        var source = new FakeSource(request.Flow, request, request.DeviceId + " name");
        Opened.Add(source);
        return Task.FromResult<ICaptureSource>(source);
    }
}

internal sealed class FakeWatcher : IDeviceWatcher
{
    public event Action? DevicesChanged;

    public void Start()
    {
    }

    public void Raise() => DevicesChanged?.Invoke();

    public void Dispose()
    {
    }
}

/// <summary>
/// An engine on fake hardware and a fake clock. Each sample is a ramp value derived from the wall-clock frame it
/// belongs to, so comparing the sources sample by sample proves they line up in time.
/// </summary>
internal sealed class AudioTestRig : IDisposable
{
    public const int PacketFrames = 480;
    public const double PacketSeconds = 0.01;

    private readonly long _epoch;

    public AudioTestRig(double bufferSeconds = 60, bool micEnabled = true, string? systemDeviceId = null, TimeSpan? tick = null)
    {
        _epoch = Clock.Ticks;
        Engine = new AudioEngine(
            new AudioEngineOptions(TimeSpan.FromSeconds(bufferSeconds), micEnabled, systemDeviceId),
            Factory,
            Watcher,
            () => Clock.Ticks,
            tick ?? TimeSpan.FromHours(1));
    }

    public FakeClock Clock { get; } = new();

    public FakeFactory Factory { get; } = new();

    public FakeWatcher Watcher { get; } = new();

    public AudioEngine Engine { get; }

    public FakeSource SystemSource => Factory.Latest(AudioFlow.System)!;

    public FakeSource MicSource => Factory.Latest(AudioFlow.Mic)!;

    public void Run(double seconds, bool system = true, bool mic = true)
    {
        var packets = (int)Math.Round(seconds / PacketSeconds);
        var systemPacket = new float[PacketFrames * 2];
        var micPacket = new float[PacketFrames];
        for (var packet = 0; packet < packets; packet++)
        {
            var stamp = Clock.Ticks;
            var wallFrame = (stamp - _epoch) * 3 / 625;
            Clock.Advance(PacketSeconds);
            for (var frame = 0; frame < PacketFrames; frame++)
            {
                var value = Ramp.Value(wallFrame + frame);
                systemPacket[frame * 2] = value;
                systemPacket[frame * 2 + 1] = -value;
                micPacket[frame] = value;
            }

            if (system)
            {
                SystemSource.Deliver(systemPacket, stamp);
            }

            if (mic)
            {
                MicSource.Deliver(micPacket, stamp);
            }
        }
    }

    public void Dispose() => Engine.Dispose();
}

/// <summary>A signal that is never zero and tells the frame it was made for, through the 16-bit buffer.</summary>
internal static class Ramp
{
    private const int Period = 20000;

    public static float Value(long frame)
    {
        return 0.1f + 0.8f * (frame % Period) / Period;
    }

    public static float ThroughBuffer(long frame) => SampleCodec.ToFloat(SampleCodec.ToShort(Value(frame)));
}
