// Throwaway probe of the macOS port: Core Audio process taps and input, called by HindsoundProbe through P/Invoke.
// Built by tools/mac-probe/probe.sh with clang. Every result is printed as "PROBE <tag>.<name> <result>".
//
// probe_audio_run(tag, tonePid, silentPid, seconds):
//   1. the permission state as the private TCC preflight and AVCaptureDevice see it, the devices, the audio processes;
//   2. plays its own 440 Hz tone on the default output, then a global tap that excludes its own process (expects the
//      1 kHz tone of afplay and no 440 Hz) and a global tap that excludes nothing (expects both);
//   3. a tap of the afplay process that plays 1 kHz (expects it), and of the afplay process that plays silence;
//   4. the default input (BlackHole, which returns what is played on its output), read directly.
// Each capture reports, per input buffer: the peak, the RMS, how many samples are not exactly zero, the frequency from the
// zero crossings, and the amplitudes at 1000 Hz and 440 Hz (Goertzel).
#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <CoreAudio/CoreAudio.h>
#import <CoreAudio/AudioHardwareTapping.h>
#import <CoreAudio/CATapDescription.h>
#include <dlfcn.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>

#define MAX_BUFFERS 8

static char g_tag[64] = "audio";

static void probe(const char *name, NSString *format, ...) NS_FORMAT_FUNCTION(2, 3);
static void probe(const char *name, NSString *format, ...) {
    va_list args;
    va_start(args, format);
    NSString *text = [[NSString alloc] initWithFormat:format arguments:args];
    va_end(args);
    printf("PROBE %s.%s %s\n", g_tag, name, text.UTF8String);
    fflush(stdout);
}

static const char *key(const char *name, const char *suffix) {
    static char buffer[160];
    snprintf(buffer, sizeof buffer, "%s%s", name, suffix);
    return buffer;
}

// --- Property helpers -------------------------------------------------------------------------------------------------

static OSStatus getProperty(AudioObjectID object, AudioObjectPropertySelector selector, AudioObjectPropertyScope scope,
                            UInt32 qualifierSize, const void *qualifier, UInt32 *size, void *data) {
    AudioObjectPropertyAddress address = { selector, scope, kAudioObjectPropertyElementMain };
    return AudioObjectGetPropertyData(object, &address, qualifierSize, qualifier, size, data);
}

static NSString *getString(AudioObjectID object, AudioObjectPropertySelector selector) {
    CFStringRef value = NULL;
    UInt32 size = sizeof value;
    if (getProperty(object, selector, kAudioObjectPropertyScopeGlobal, 0, NULL, &size, &value) != noErr || value == NULL) {
        return @"?";
    }
    return (__bridge_transfer NSString *)value;
}

static AudioObjectID getDevice(AudioObjectPropertySelector selector) {
    AudioObjectID device = kAudioObjectUnknown;
    UInt32 size = sizeof device;
    getProperty(kAudioObjectSystemObject, selector, kAudioObjectPropertyScopeGlobal, 0, NULL, &size, &device);
    return device;
}

static Float64 nominalRate(AudioObjectID device) {
    Float64 rate = 0;
    UInt32 size = sizeof rate;
    getProperty(device, kAudioDevicePropertyNominalSampleRate, kAudioObjectPropertyScopeGlobal, 0, NULL, &size, &rate);
    return rate;
}

static UInt32 inputStreamCount(AudioObjectID device) {
    AudioObjectPropertyAddress address = { kAudioDevicePropertyStreams, kAudioObjectPropertyScopeInput,
                                           kAudioObjectPropertyElementMain };
    UInt32 size = 0;
    if (AudioObjectGetPropertyDataSize(device, &address, 0, NULL, &size) != noErr) return 0;
    return size / sizeof(AudioStreamID);
}

static AudioObjectID processObject(pid_t pid) {
    AudioObjectID object = kAudioObjectUnknown;
    UInt32 size = sizeof object;
    getProperty(kAudioObjectSystemObject, kAudioHardwarePropertyTranslatePIDToProcessObject,
                kAudioObjectPropertyScopeGlobal, sizeof pid, &pid, &size, &object);
    return object;
}

static void listProcesses(void) {
    AudioObjectPropertyAddress address = { kAudioHardwarePropertyProcessObjectList, kAudioObjectPropertyScopeGlobal,
                                           kAudioObjectPropertyElementMain };
    UInt32 size = 0;
    OSStatus status = AudioObjectGetPropertyDataSize(kAudioObjectSystemObject, &address, 0, NULL, &size);
    if (status != noErr) {
        probe("process_objects", @"status=%d", (int)status);
        return;
    }
    UInt32 count = size / sizeof(AudioObjectID);
    AudioObjectID *objects = calloc(count ? count : 1, sizeof(AudioObjectID));
    AudioObjectGetPropertyData(kAudioObjectSystemObject, &address, 0, NULL, &size, objects);
    NSMutableArray *items = [NSMutableArray array];
    for (UInt32 i = 0; i < count; i++) {
        pid_t pid = 0;
        UInt32 running = 0;
        UInt32 s = sizeof pid;
        getProperty(objects[i], kAudioProcessPropertyPID, kAudioObjectPropertyScopeGlobal, 0, NULL, &s, &pid);
        s = sizeof running;
        getProperty(objects[i], kAudioProcessPropertyIsRunningOutput, kAudioObjectPropertyScopeGlobal, 0, NULL, &s, &running);
        [items addObject:[NSString stringWithFormat:@"%u:pid%d:%@:out%u", objects[i], pid,
                          getString(objects[i], kAudioProcessPropertyBundleID), running]];
    }
    free(objects);
    probe("process_objects", @"count=%u %@", count, [items componentsJoinedByString:@","]);
}

// --- Capture and analysis ---------------------------------------------------------------------------------------------

typedef struct {
    UInt32 channels;
    UInt64 frames, samples, nonzero, crossings;
    int last;
    double peak, sumsq;
    double g440[2], g1000[2];
} BufferStats;

typedef struct {
    double rate, c440, c1000;
    UInt64 callbacks;
    UInt32 buffers;
    BufferStats b[MAX_BUFFERS];
} Stats;

static OSStatus inputProc(AudioObjectID device, const AudioTimeStamp *now, const AudioBufferList *input,
                          const AudioTimeStamp *inputTime, AudioBufferList *output, const AudioTimeStamp *outputTime,
                          void *context) {
    Stats *s = context;
    s->callbacks++;
    if (input == NULL) return noErr;
    UInt32 n = input->mNumberBuffers < MAX_BUFFERS ? input->mNumberBuffers : MAX_BUFFERS;
    if (n > s->buffers) s->buffers = n;
    for (UInt32 i = 0; i < n; i++) {
        const AudioBuffer *buffer = &input->mBuffers[i];
        const float *data = buffer->mData;
        if (data == NULL) continue;
        BufferStats *b = &s->b[i];
        UInt32 channels = buffer->mNumberChannels ? buffer->mNumberChannels : 1;
        UInt32 count = buffer->mDataByteSize / sizeof(float);
        UInt32 frames = count / channels;
        b->channels = channels;
        b->samples += count;
        b->frames += frames;
        for (UInt32 k = 0; k < count; k++) {
            float v = data[k];
            if (v != 0.0f) b->nonzero++;
            double a = fabs(v);
            if (a > b->peak) b->peak = a;
            b->sumsq += (double)v * v;
        }
        for (UInt32 f = 0; f < frames; f++) {
            double v = data[f * channels];
            int sign = v > 0.01 ? 1 : (v < -0.01 ? -1 : 0);
            if (sign != 0) {
                if (sign == 1 && b->last == -1) b->crossings++;
                b->last = sign;
            }
            double g = v + s->c440 * b->g440[0] - b->g440[1];
            b->g440[1] = b->g440[0];
            b->g440[0] = g;
            g = v + s->c1000 * b->g1000[0] - b->g1000[1];
            b->g1000[1] = b->g1000[0];
            b->g1000[0] = g;
        }
    }
    return noErr;
}

static double amplitude(const double g[2], double c, UInt64 n) {
    if (n == 0) return 0;
    double power = g[0] * g[0] + g[1] * g[1] - c * g[0] * g[1];
    return 2.0 * sqrt(power > 0 ? power : 0) / (double)n;
}

// Reads the input of a device for some seconds; returns the number of IO callbacks.
static UInt64 capture(const char *name, AudioObjectID device, double rate, double seconds) {
    Stats *s = calloc(1, sizeof(Stats));
    s->rate = rate > 0 ? rate : 48000;
    s->c440 = 2 * cos(2 * M_PI * 440 / s->rate);
    s->c1000 = 2 * cos(2 * M_PI * 1000 / s->rate);
    AudioDeviceIOProcID proc = NULL;
    OSStatus created = AudioDeviceCreateIOProcID(device, inputProc, s, &proc);
    OSStatus started = created != noErr ? created : AudioDeviceStart(device, proc);
    probe(key(name, ".start"), @"createIOProc=%d start=%d rate=%.0f", (int)created, (int)started, s->rate);
    if (started == noErr) usleep((useconds_t)(seconds * 1e6));
    if (proc != NULL) {
        AudioDeviceStop(device, proc);
        AudioDeviceDestroyIOProcID(device, proc);
    }
    int best = -1;
    double bestPeak = -1;
    for (UInt32 i = 0; i < s->buffers; i++) {
        BufferStats *b = &s->b[i];
        double rms = b->samples ? sqrt(b->sumsq / b->samples) : 0;
        double zc = b->frames ? b->crossings / (b->frames / s->rate) : 0;
        char suffix[16];
        snprintf(suffix, sizeof suffix, ".buf%u", i);
        probe(key(name, suffix), @"ch=%u frames=%llu nonzero=%llu/%llu peak=%.4f rms=%.4f zc_freq=%.1f a1000=%.4f a440=%.4f",
              b->channels, b->frames, b->nonzero, b->samples, b->peak, rms, zc,
              amplitude(b->g1000, s->c1000, b->frames), amplitude(b->g440, s->c440, b->frames));
        if (b->peak > bestPeak) {
            bestPeak = b->peak;
            best = (int)i;
        }
    }
    UInt64 callbacks = s->callbacks;
    if (started != noErr) {
        probe(key(name, ".result"), @"ERROR start=%d", (int)started);
    } else if (callbacks == 0 || best < 0) {
        probe(key(name, ".result"), @"NO_DATA callbacks=%llu", callbacks);
    } else {
        BufferStats *b = &s->b[best];
        double zc = b->frames ? b->crossings / (b->frames / s->rate) : 0;
        const char *verdict = b->peak == 0 ? "SILENCE_EXACT_ZERO" : (b->peak < 0.001 ? "SILENCE_NEAR_ZERO" : "SIGNAL");
        probe(key(name, ".result"), @"%s callbacks=%llu buffer=%d peak=%.4f zc_freq=%.1f a1000=%.4f a440=%.4f", verdict,
              callbacks, best, b->peak, zc, amplitude(b->g1000, s->c1000, b->frames), amplitude(b->g440, s->c440, b->frames));
    }
    free(s);
    return callbacks;
}

// --- Taps -------------------------------------------------------------------------------------------------------------

static void tapTest(const char *name, CATapDescription *description, AudioObjectID outputDevice, double seconds) {
    description.muteBehavior = CATapUnmuted;
    AudioObjectID tap = kAudioObjectUnknown;
    OSStatus status = AudioHardwareCreateProcessTap(description, &tap);
    probe(key(name, ".create"), @"status=%d tap=%u", (int)status, tap);
    if (status != noErr) {
        probe(key(name, ".result"), @"ERROR create=%d", (int)status);
        return;
    }
    // The tap's own properties: a missing permission might show here as an error (forum report), or not at all.
    AudioStreamBasicDescription format = { 0 };
    UInt32 size = sizeof format;
    OSStatus formatStatus = getProperty(tap, kAudioTapPropertyFormat, kAudioObjectPropertyScopeGlobal, 0, NULL, &size, &format);
    CFTypeRef read = NULL;
    size = sizeof read;
    OSStatus descriptionStatus = getProperty(tap, kAudioTapPropertyDescription, kAudioObjectPropertyScopeGlobal, 0, NULL,
                                             &size, &read);
    if (read != NULL) CFRelease(read);
    NSString *tapUID = getString(tap, kAudioTapPropertyUID);
    probe(key(name, ".tap"), @"format_status=%d rate=%.0f channels=%u flags=0x%x bits=%u description_status=%d uid=%@",
          (int)formatStatus, format.mSampleRate, format.mChannelsPerFrame, format.mFormatFlags, format.mBitsPerChannel,
          (int)descriptionStatus, tapUID);
    if ([tapUID isEqualToString:@"?"]) tapUID = description.UUID.UUIDString;

    NSString *outputUID = getString(outputDevice, kAudioDevicePropertyDeviceUID);
    // First an aggregate of the tap alone, so that nothing but the tap feeds it (BlackHole's own input would give the
    // tone without any permission); then, only if that one delivers nothing, the aggregate with the output device.
    for (int variant = 0; variant < 2; variant++) {
        NSMutableDictionary *aggregate = [@{
            @kAudioAggregateDeviceNameKey: @"HindsoundProbeTap",
            @kAudioAggregateDeviceUIDKey: [NSUUID UUID].UUIDString,
            @kAudioAggregateDeviceIsPrivateKey: @YES,
            @kAudioAggregateDeviceIsStackedKey: @NO,
            @kAudioAggregateDeviceTapAutoStartKey: @YES,
            @kAudioAggregateDeviceTapListKey: @[ @{ @kAudioSubTapDriftCompensationKey: @YES, @kAudioSubTapUIDKey: tapUID } ],
        } mutableCopy];
        if (variant == 1) {
            aggregate[@kAudioAggregateDeviceMainSubDeviceKey] = outputUID;
            aggregate[@kAudioAggregateDeviceSubDeviceListKey] = @[ @{ @kAudioSubDeviceUIDKey: outputUID } ];
        }
        AudioObjectID device = kAudioObjectUnknown;
        status = AudioHardwareCreateAggregateDevice((__bridge CFDictionaryRef)aggregate, &device);
        const char *variantName = variant == 0 ? "" : "_with_output";
        char sub[128];
        snprintf(sub, sizeof sub, "%s%s", name, variantName);
        probe(key(sub, ".aggregate"), @"status=%d device=%u input_streams=%u", (int)status, device,
              status == noErr ? inputStreamCount(device) : 0);
        if (status != noErr) continue;
        double rate = format.mSampleRate > 0 ? format.mSampleRate : nominalRate(device);
        UInt64 callbacks = capture(sub, device, rate, seconds);
        AudioHardwareDestroyAggregateDevice(device);
        if (callbacks > 0) break;
    }
    AudioHardwareDestroyProcessTap(tap);
}

// --- Our own tone, so that our process has an audio object to exclude ----------------------------------------------

typedef struct {
    double phase, step;
} Tone;

static OSStatus outputProc(AudioObjectID device, const AudioTimeStamp *now, const AudioBufferList *input,
                           const AudioTimeStamp *inputTime, AudioBufferList *output, const AudioTimeStamp *outputTime,
                           void *context) {
    Tone *tone = context;
    double end = tone->phase;
    for (UInt32 i = 0; output != NULL && i < output->mNumberBuffers; i++) {
        float *data = output->mBuffers[i].mData;
        if (data == NULL) continue;
        UInt32 channels = output->mBuffers[i].mNumberChannels ? output->mBuffers[i].mNumberChannels : 1;
        UInt32 frames = output->mBuffers[i].mDataByteSize / (sizeof(float) * channels);
        double phase = tone->phase;
        for (UInt32 f = 0; f < frames; f++) {
            float v = (float)(0.3 * sin(phase));
            phase += tone->step;
            for (UInt32 c = 0; c < channels; c++) data[f * channels + c] = v;
        }
        end = phase;
    }
    tone->phase = fmod(end, 2 * M_PI);
    return noErr;
}

// --- Permissions --------------------------------------------------------------------------------------------------------

static void permissions(const char *when) {
    typedef int (*Preflight)(CFStringRef, CFDictionaryRef);
    void *tcc = dlopen("/System/Library/PrivateFrameworks/TCC.framework/Versions/A/TCC", RTLD_NOW);
    Preflight preflight = tcc ? (Preflight)dlsym(tcc, "TCCAccessPreflight") : NULL;
    if (preflight != NULL) {
        // 0 granted, 1 denied, 2 not determined (as AudioCap reads it).
        probe(key("tcc_preflight_", when), @"kTCCServiceAudioCapture=%d kTCCServiceMicrophone=%d",
              preflight(CFSTR("kTCCServiceAudioCapture"), NULL), preflight(CFSTR("kTCCServiceMicrophone"), NULL));
    } else {
        probe(key("tcc_preflight_", when), @"UNAVAILABLE %s", dlerror());
    }
    // 0 notDetermined, 1 restricted, 2 denied, 3 authorized.
    probe(key("av_microphone_", when), @"%ld", (long)[AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeAudio]);
}

__attribute__((visibility("default")))
int probe_audio_run(const char *tag, int tonePid, int silentPid, double seconds) {
    @autoreleasepool {
        snprintf(g_tag, sizeof g_tag, "%s", tag);
        permissions("before");
        AudioObjectID output = getDevice(kAudioHardwarePropertyDefaultOutputDevice);
        AudioObjectID input = getDevice(kAudioHardwarePropertyDefaultInputDevice);
        probe("devices", @"output=%u:%@ (%@) input=%u:%@", output, getString(output, kAudioObjectPropertyName),
              getString(output, kAudioDevicePropertyDeviceUID), input, getString(input, kAudioObjectPropertyName));
        listProcesses();

        // 1. Global taps, while we play 440 Hz ourselves and afplay plays 1 kHz.
        Tone *tone = calloc(1, sizeof(Tone));
        double outRate = nominalRate(output);
        tone->step = 2 * M_PI * 440 / (outRate > 0 ? outRate : 48000);
        AudioDeviceIOProcID toneProc = NULL;
        OSStatus toneCreated = AudioDeviceCreateIOProcID(output, outputProc, tone, &toneProc);
        OSStatus toneStarted = toneCreated != noErr ? toneCreated : AudioDeviceStart(output, toneProc);
        usleep(500000);
        AudioObjectID selfObject = processObject(getpid());
        probe("self_tone", @"create=%d start=%d self_object=%u", (int)toneCreated, (int)toneStarted, selfObject);
        NSArray *exclude = selfObject != kAudioObjectUnknown ? @[ @(selfObject) ] : @[];
        tapTest("tap_global_excl_self", [[CATapDescription alloc] initStereoGlobalTapButExcludeProcesses:exclude], output,
                seconds);
        tapTest("tap_global_all", [[CATapDescription alloc] initStereoGlobalTapButExcludeProcesses:@[]], output, seconds);
        if (toneProc != NULL) {
            AudioDeviceStop(output, toneProc);
            AudioDeviceDestroyIOProcID(output, toneProc);
        }

        // 2. One application: the afplay that plays 1 kHz, and the one that plays silence.
        AudioObjectID toneObject = processObject(tonePid);
        AudioObjectID silentObject = processObject(silentPid);
        probe("process_targets", @"tone_pid=%d object=%u silent_pid=%d object=%u", tonePid, toneObject, silentPid,
              silentObject);
        if (toneObject != kAudioObjectUnknown) {
            tapTest("tap_proc_tone", [[CATapDescription alloc] initStereoMixdownOfProcesses:@[ @(toneObject) ]], output,
                    seconds);
        } else {
            probe("tap_proc_tone.result", @"NO_PROCESS_OBJECT pid=%d", tonePid);
        }
        if (silentObject != kAudioObjectUnknown) {
            tapTest("tap_proc_silent", [[CATapDescription alloc] initStereoMixdownOfProcesses:@[ @(silentObject) ]], output,
                    seconds);
        } else {
            probe("tap_proc_silent.result", @"NO_PROCESS_OBJECT pid=%d", silentPid);
        }

        // 3. The microphone: the default input, BlackHole, which gives back what afplay plays on its output.
        if (input != kAudioObjectUnknown) {
            capture("mic_default_input", input, nominalRate(input), seconds);
        } else {
            probe("mic_default_input.result", @"NO_INPUT_DEVICE");
        }
        permissions("after");
    }
    return 0;
}
