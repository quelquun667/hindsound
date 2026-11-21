// Throwaway probe of the macOS port: one Core Audio test per process, so that a hang stops only that test.
// Built twice by tools/mac-probe/probe.sh (PROBE_BUILD 1 and 2: different binaries), run as a plain program or as the
// executable of HindsoundProbe.app. Every result is printed as "PROBE <tag>.<name> <result>".
//
// usage: capture <tag> <step-limit-s> <capture-s> <sample-path> <hal-runloop: null|main> <test> [args]
//   perm                                    TCC preflight (private) and AVCaptureDevice status
//   out_ioproc                              an output IOProc on the default output (440 Hz): does any start return
//   in_ioproc                               an input IOProc on the default input (BlackHole gives back its output)
//   in_queue                                an AudioQueue input on the default input
//   tap <scope> <aggregate> <reader> [pid]  a process tap read through an aggregate device
//       scope:     global (excludes nothing), global_excl_self (excludes this process), proc (the process of pid)
//       aggregate: taponly (the tap alone), out (main sub-device: the default output, as AudioCap), null (main
//                  sub-device: the Null Audio Device, whose input is silent, so only the tap can bring sound)
//       reader:    ioproc, auhal (kAudioUnitSubType_HALOutput), queue (AudioQueue input on the aggregate)
//       The global scopes play a 440 Hz tone of this process (AudioQueue output) during the capture.
// Each step prints a line before and after; a step that lasts longer than the limit prints "stuck", writes a sample of
// this process to <sample-path> and exits with 3. Each capture prints, per channel, the level in dBFS, the dominant
// frequency (Goertzel scan, 50 to 6000 Hz) and the amplitudes at 1000 Hz and 440 Hz.
#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <AudioToolbox/AudioToolbox.h>
#import <CoreAudio/CoreAudio.h>
#import <CoreAudio/AudioHardwareTapping.h>
#import <CoreAudio/CATapDescription.h>
#include <dlfcn.h>
#include <math.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#ifndef PROBE_BUILD
#define PROBE_BUILD 1
#endif
#define STR2(x) #x
#define STR(x) STR2(x)
static const char *const kMark = "hindsound-probe-build-" STR(PROBE_BUILD);

#define MAX_CH 8
#define KEEP 16384
#define SKIP 4800
#define MAX_FRAMES 4096

static char g_tag[96] = "capture";
static double g_t0, g_stepLimit = 15, g_seconds = 2;
static const char *g_samplePath = "";
static volatile double g_stepStart;
static char g_step[96];
static volatile int g_finished;

static double now(void) { return CFAbsoluteTimeGetCurrent(); }

static void probe(const char *name, NSString *format, ...) NS_FORMAT_FUNCTION(2, 3);
static void probe(const char *name, NSString *format, ...) {
    va_list args;
    va_start(args, format);
    NSString *text = [[NSString alloc] initWithFormat:format arguments:args];
    va_end(args);
    printf("PROBE %s.%s %s\n", g_tag, name, text.UTF8String);
    fflush(stdout);
}

static void begin(const char *what) {
    snprintf(g_step, sizeof g_step, "%s", what);
    probe("step", @"%s begin t=%.2f", what, now() - g_t0);
    g_stepStart = now();
}

static void end(const char *what, OSStatus status) {
    double ms = (now() - g_stepStart) * 1000;
    g_stepStart = 0;
    probe("step", @"%s end status=%d ms=%.0f", what, (int)status, ms);
}

static void waitSeconds(const char *what, double seconds) {
    begin(what);
    usleep((useconds_t)(seconds * 1e6));
    end(what, 0);
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

static UInt32 inputChannels(AudioObjectID device) {
    AudioObjectPropertyAddress address = { kAudioDevicePropertyStreamConfiguration, kAudioObjectPropertyScopeInput,
                                           kAudioObjectPropertyElementMain };
    UInt32 size = 0;
    if (AudioObjectGetPropertyDataSize(device, &address, 0, NULL, &size) != noErr || size == 0) return 0;
    AudioBufferList *list = malloc(size);
    UInt32 channels = 0;
    if (AudioObjectGetPropertyData(device, &address, 0, NULL, &size, list) == noErr) {
        for (UInt32 i = 0; i < list->mNumberBuffers; i++) channels += list->mBuffers[i].mNumberChannels;
    }
    free(list);
    return channels;
}

static AudioObjectID processObject(pid_t pid) {
    AudioObjectID object = kAudioObjectUnknown;
    UInt32 size = sizeof object;
    getProperty(kAudioObjectSystemObject, kAudioHardwarePropertyTranslatePIDToProcessObject,
                kAudioObjectPropertyScopeGlobal, sizeof pid, &pid, &size, &object);
    return object;
}

// --- Analysis -----------------------------------------------------------------------------------------------------------

typedef struct {
    UInt64 frames, nonzero;
    double peak, sumsq;
    UInt32 kept;
    float keep[KEEP];
} Chan;

typedef struct {
    double rate, firstCallback;
    UInt64 callbacks;
    UInt32 channels;
    OSStatus renderError;
    Chan c[MAX_CH];
} Stats;

static Stats *newStats(double rate) {
    Stats *s = calloc(1, sizeof(Stats));
    s->rate = rate > 0 ? rate : 48000;
    return s;
}

static void markCallback(Stats *s) {
    if (s->callbacks++ == 0) s->firstCallback = now();
}

static void addInterleaved(Stats *s, UInt32 base, const float *data, UInt32 channels, UInt32 frames) {
    if (data == NULL || channels == 0) return;
    for (UInt32 c = 0; c < channels; c++) {
        UInt32 index = base + c;
        if (index >= MAX_CH) break;
        if (index + 1 > s->channels) s->channels = index + 1;
        Chan *k = &s->c[index];
        for (UInt32 f = 0; f < frames; f++) {
            float v = data[f * channels + c];
            if (v != 0.0f) k->nonzero++;
            double a = fabs(v);
            if (a > k->peak) k->peak = a;
            k->sumsq += (double)v * v;
            if (k->frames >= SKIP && k->kept < KEEP) k->keep[k->kept++] = v;
            k->frames++;
        }
    }
}

static double goertzel(const float *x, UInt32 n, double freq, double rate) {
    if (n == 0) return 0;
    double c = 2 * cos(2 * M_PI * freq / rate), s1 = 0, s2 = 0;
    for (UInt32 i = 0; i < n; i++) {
        double s0 = x[i] + c * s1 - s2;
        s2 = s1;
        s1 = s0;
    }
    double power = s1 * s1 + s2 * s2 - c * s1 * s2;
    return 2 * sqrt(power > 0 ? power : 0) / n;
}

static double dB(double a) { return a > 0 ? 20 * log10(a) : -999; }

static void report(const char *name, Stats *s, OSStatus started, double startedAt) {
    char key[160];
    int best = -1;
    double bestPeak = -1, dominant[MAX_CH] = { 0 }, dominantAmp[MAX_CH] = { 0 };
    for (UInt32 i = 0; i < s->channels; i++) {
        Chan *k = &s->c[i];
        if (k->kept >= 1024) {
            for (double f = 50; f <= 6000; f += 5) {
                double a = goertzel(k->keep, k->kept, f, s->rate);
                if (a > dominantAmp[i]) {
                    dominantAmp[i] = a;
                    dominant[i] = f;
                }
            }
        }
        double rms = k->frames ? sqrt(k->sumsq / k->frames) : 0;
        snprintf(key, sizeof key, "%s.ch%u", name, i);
        probe(key, @"frames=%llu nonzero=%llu peak=%.1fdBFS rms=%.1fdBFS dominant=%.0fHz@%.1fdBFS a1000=%.1fdBFS a440=%.1fdBFS",
              k->frames, k->nonzero, dB(k->peak), dB(rms), dominant[i], dB(dominantAmp[i]),
              dB(goertzel(k->keep, k->kept, 1000, s->rate)), dB(goertzel(k->keep, k->kept, 440, s->rate)));
        if (k->peak > bestPeak) {
            bestPeak = k->peak;
            best = (int)i;
        }
    }
    snprintf(key, sizeof key, "%s.result", name);
    double first = s->callbacks ? (s->firstCallback - startedAt) * 1000 : -1;
    if (started != noErr) {
        probe(key, @"ERROR start=%d", (int)started);
    } else if (s->callbacks == 0 || best < 0) {
        probe(key, @"NO_DATA callbacks=%llu render_error=%d", s->callbacks, (int)s->renderError);
    } else {
        Chan *k = &s->c[best];
        const char *verdict = k->peak == 0 ? "SILENCE_EXACT_ZERO" : (k->peak < 0.001 ? "SILENCE_NEAR_ZERO" : "SIGNAL");
        probe(key, @"%s callbacks=%llu first_callback_ms=%.0f channels=%u best_ch=%d peak=%.1fdBFS dominant=%.0fHz a1000=%.1fdBFS a440=%.1fdBFS",
              verdict, s->callbacks, first, s->channels, best, dB(k->peak), dominant[best],
              dB(goertzel(k->keep, k->kept, 1000, s->rate)), dB(goertzel(k->keep, k->kept, 440, s->rate)));
    }
}

// --- Readers ------------------------------------------------------------------------------------------------------------

static OSStatus inputProc(AudioObjectID device, const AudioTimeStamp *now_, const AudioBufferList *input,
                          const AudioTimeStamp *inputTime, AudioBufferList *output, const AudioTimeStamp *outputTime,
                          void *context) {
    Stats *s = context;
    markCallback(s);
    if (input == NULL) return noErr;
    UInt32 base = 0;
    for (UInt32 i = 0; i < input->mNumberBuffers; i++) {
        const AudioBuffer *b = &input->mBuffers[i];
        UInt32 channels = b->mNumberChannels ? b->mNumberChannels : 1;
        addInterleaved(s, base, b->mData, channels, b->mDataByteSize / (UInt32)(sizeof(float) * channels));
        base += channels;
    }
    return noErr;
}

static void readIOProc(const char *name, AudioObjectID device) {
    Stats *s = newStats(nominalRate(device));
    AudioDeviceIOProcID proc = NULL;
    begin("create_ioproc");
    OSStatus status = AudioDeviceCreateIOProcID(device, inputProc, s, &proc);
    end("create_ioproc", status);
    double startedAt = now();
    if (status == noErr) {
        begin("device_start");
        status = AudioDeviceStart(device, proc);
        end("device_start", status);
        if (status == noErr) {
            waitSeconds("capturing", g_seconds);
            begin("device_stop");
            end("device_stop", AudioDeviceStop(device, proc));
        }
        AudioDeviceDestroyIOProcID(device, proc);
    }
    report(name, s, status, startedAt);
    free(s);
}

typedef struct {
    AudioUnit unit;
    Stats *s;
    UInt32 channels;
    AudioBufferList *list;
} Hal;

static OSStatus halInput(void *ref, AudioUnitRenderActionFlags *flags, const AudioTimeStamp *time, UInt32 bus,
                         UInt32 frames, AudioBufferList *unused) {
    Hal *h = ref;
    markCallback(h->s);
    if (frames > MAX_FRAMES) frames = MAX_FRAMES;
    for (UInt32 i = 0; i < h->channels; i++) h->list->mBuffers[i].mDataByteSize = frames * sizeof(float);
    OSStatus status = AudioUnitRender(h->unit, flags, time, bus, frames, h->list);
    if (status != noErr) {
        h->s->renderError = status;
        return status;
    }
    for (UInt32 i = 0; i < h->channels; i++) addInterleaved(h->s, i, h->list->mBuffers[i].mData, 1, frames);
    return noErr;
}

static void readAUHAL(const char *name, AudioObjectID device) {
    Hal h = { 0 };
    OSStatus status;
    AudioComponentDescription d = { kAudioUnitType_Output, kAudioUnitSubType_HALOutput, kAudioUnitManufacturer_Apple, 0, 0 };
    begin("au_new");
    AudioComponent component = AudioComponentFindNext(NULL, &d);
    status = component ? AudioComponentInstanceNew(component, &h.unit) : -1;
    end("au_new", status);
    double startedAt = now();
    Stats *s = newStats(nominalRate(device));
    h.s = s;
    if (status == noErr) {
        UInt32 one = 1, zero = 0;
        begin("au_setup");
        status = AudioUnitSetProperty(h.unit, kAudioOutputUnitProperty_EnableIO, kAudioUnitScope_Input, 1, &one, sizeof one);
        if (status == noErr)
            status = AudioUnitSetProperty(h.unit, kAudioOutputUnitProperty_EnableIO, kAudioUnitScope_Output, 0, &zero, sizeof zero);
        if (status == noErr)
            status = AudioUnitSetProperty(h.unit, kAudioOutputUnitProperty_CurrentDevice, kAudioUnitScope_Global, 0, &device,
                                          sizeof device);
        AudioStreamBasicDescription hw = { 0 };
        UInt32 size = sizeof hw;
        if (status == noErr)
            status = AudioUnitGetProperty(h.unit, kAudioUnitProperty_StreamFormat, kAudioUnitScope_Input, 1, &hw, &size);
        h.channels = hw.mChannelsPerFrame > MAX_CH ? MAX_CH : hw.mChannelsPerFrame;
        if (h.channels == 0) h.channels = 2;
        if (hw.mSampleRate > 0) s->rate = hw.mSampleRate;
        AudioStreamBasicDescription client = { s->rate, kAudioFormatLinearPCM,
            kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked | kAudioFormatFlagIsNonInterleaved, 4, 1, 4, h.channels, 32, 0 };
        if (status == noErr)
            status = AudioUnitSetProperty(h.unit, kAudioUnitProperty_StreamFormat, kAudioUnitScope_Output, 1, &client,
                                          sizeof client);
        h.list = calloc(1, offsetof(AudioBufferList, mBuffers) + sizeof(AudioBuffer) * h.channels);
        h.list->mNumberBuffers = h.channels;
        for (UInt32 i = 0; i < h.channels; i++) {
            h.list->mBuffers[i].mNumberChannels = 1;
            h.list->mBuffers[i].mData = calloc(MAX_FRAMES, sizeof(float));
            h.list->mBuffers[i].mDataByteSize = MAX_FRAMES * sizeof(float);
        }
        AURenderCallbackStruct callback = { halInput, &h };
        if (status == noErr)
            status = AudioUnitSetProperty(h.unit, kAudioOutputUnitProperty_SetInputCallback, kAudioUnitScope_Global, 0,
                                          &callback, sizeof callback);
        end("au_setup", status);
        probe("au_format", @"hw_rate=%.0f hw_channels=%u", hw.mSampleRate, hw.mChannelsPerFrame);
        if (status == noErr) {
            begin("au_initialize");
            status = AudioUnitInitialize(h.unit);
            end("au_initialize", status);
        }
        if (status == noErr) {
            begin("au_start");
            startedAt = now();
            status = AudioOutputUnitStart(h.unit);
            end("au_start", status);
            if (status == noErr) {
                waitSeconds("capturing", g_seconds);
                begin("au_stop");
                end("au_stop", AudioOutputUnitStop(h.unit));
            }
        }
        AudioComponentInstanceDispose(h.unit);
    }
    report(name, s, status, startedAt);
    free(s);
}

typedef struct {
    Stats *s;
    UInt32 channels;
    volatile int running;
} Queue;

static void queueInput(void *user, AudioQueueRef queue, AudioQueueBufferRef buffer, const AudioTimeStamp *time,
                       UInt32 packets, const AudioStreamPacketDescription *descriptions) {
    Queue *q = user;
    markCallback(q->s);
    addInterleaved(q->s, 0, buffer->mAudioData, q->channels,
                   buffer->mAudioDataByteSize / (UInt32)(sizeof(float) * q->channels));
    if (q->running) AudioQueueEnqueueBuffer(queue, buffer, 0, NULL);
}

static void readQueue(const char *name, AudioObjectID device, BOOL setDevice) {
    Queue q = { 0 };
    q.s = newStats(nominalRate(device));
    q.channels = inputChannels(device);
    if (q.channels == 0) q.channels = 2;
    if (q.channels > MAX_CH) q.channels = MAX_CH;
    AudioStreamBasicDescription format = { q.s->rate, kAudioFormatLinearPCM, kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked,
                                           4 * q.channels, 1, 4 * q.channels, q.channels, 32, 0 };
    AudioQueueRef queue = NULL;
    begin("queue_new");
    OSStatus status = AudioQueueNewInput(&format, queueInput, &q, NULL, kCFRunLoopCommonModes, 0, &queue);
    end("queue_new", status);
    probe("queue_format", @"rate=%.0f channels=%u", q.s->rate, q.channels);
    double startedAt = now();
    if (status == noErr) {
        if (setDevice) {
            NSString *uidString = getString(device, kAudioDevicePropertyDeviceUID);
            CFStringRef uid = (__bridge CFStringRef)uidString;
            begin("queue_set_device");
            status = AudioQueueSetProperty(queue, kAudioQueueProperty_CurrentDevice, &uid, sizeof uid);
            end("queue_set_device", status);
        }
        for (int i = 0; i < 3 && status == noErr; i++) {
            AudioQueueBufferRef buffer = NULL;
            status = AudioQueueAllocateBuffer(queue, (UInt32)(q.s->rate / 10) * 4 * q.channels, &buffer);
            if (status == noErr) status = AudioQueueEnqueueBuffer(queue, buffer, 0, NULL);
        }
        if (status == noErr) {
            q.running = 1;
            begin("queue_start");
            startedAt = now();
            status = AudioQueueStart(queue, NULL);
            end("queue_start", status);
            if (status == noErr) waitSeconds("capturing", g_seconds);
            q.running = 0;
            begin("queue_stop");
            end("queue_stop", AudioQueueStop(queue, true));
        }
        AudioQueueDispose(queue, true);
    }
    report(name, q.s, status, startedAt);
    free(q.s);
}

// --- Our own tone (440 Hz), so that this process has an audio object to exclude or to hear --------------------------

typedef struct {
    double phase, step;
    UInt64 callbacks;
} Tone;

static void fillTone(Tone *t, float *data, UInt32 frames, UInt32 channels) {
    for (UInt32 f = 0; f < frames; f++) {
        float v = (float)(0.3 * sin(t->phase));
        t->phase = fmod(t->phase + t->step, 2 * M_PI);
        for (UInt32 c = 0; c < channels; c++) data[f * channels + c] = v;
    }
}

static void queueOutput(void *user, AudioQueueRef queue, AudioQueueBufferRef buffer) {
    Tone *t = user;
    t->callbacks++;
    UInt32 frames = buffer->mAudioDataBytesCapacity / 8;
    fillTone(t, buffer->mAudioData, frames, 2);
    buffer->mAudioDataByteSize = frames * 8;
    AudioQueueEnqueueBuffer(queue, buffer, 0, NULL);
}

static AudioQueueRef startTone(Tone *t) {
    AudioStreamBasicDescription format = { 48000, kAudioFormatLinearPCM, kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked,
                                           8, 1, 8, 2, 32, 0 };
    t->step = 2 * M_PI * 440 / 48000;
    AudioQueueRef queue = NULL;
    begin("tone_new");
    OSStatus status = AudioQueueNewOutput(&format, queueOutput, t, NULL, kCFRunLoopCommonModes, 0, &queue);
    end("tone_new", status);
    for (int i = 0; i < 3 && status == noErr; i++) {
        AudioQueueBufferRef buffer = NULL;
        status = AudioQueueAllocateBuffer(queue, 4800 * 8, &buffer);
        if (status == noErr) queueOutput(t, queue, buffer);
    }
    if (status == noErr) {
        begin("tone_start");
        status = AudioQueueStart(queue, NULL);
        end("tone_start", status);
    }
    if (status != noErr) {
        if (queue) AudioQueueDispose(queue, true);
        return NULL;
    }
    waitSeconds("tone_settle", 0.5);
    return queue;
}

static OSStatus outputProc(AudioObjectID device, const AudioTimeStamp *now_, const AudioBufferList *input,
                           const AudioTimeStamp *inputTime, AudioBufferList *output, const AudioTimeStamp *outputTime,
                           void *context) {
    Tone *t = context;
    t->callbacks++;
    for (UInt32 i = 0; output != NULL && i < output->mNumberBuffers; i++) {
        UInt32 channels = output->mBuffers[i].mNumberChannels ? output->mBuffers[i].mNumberChannels : 1;
        if (output->mBuffers[i].mData == NULL) continue;
        fillTone(t, output->mBuffers[i].mData, output->mBuffers[i].mDataByteSize / (UInt32)(sizeof(float) * channels), channels);
    }
    return noErr;
}

static void outIOProc(void) {
    AudioObjectID device = getDevice(kAudioHardwarePropertyDefaultOutputDevice);
    Tone *t = calloc(1, sizeof(Tone));
    double rate = nominalRate(device);
    t->step = 2 * M_PI * 440 / (rate > 0 ? rate : 48000);
    AudioDeviceIOProcID proc = NULL;
    begin("create_ioproc");
    OSStatus status = AudioDeviceCreateIOProcID(device, outputProc, t, &proc);
    end("create_ioproc", status);
    if (status == noErr) {
        begin("device_start");
        status = AudioDeviceStart(device, proc);
        end("device_start", status);
        if (status == noErr) {
            waitSeconds("playing", g_seconds);
            begin("device_stop");
            end("device_stop", AudioDeviceStop(device, proc));
        }
        AudioDeviceDestroyIOProcID(device, proc);
    }
    probe("out_ioproc.result", @"%s start=%d callbacks=%llu device=%u:%@", status == noErr && t->callbacks > 0 ? "RUNS" : "NO_IO",
          (int)status, t->callbacks, device, getString(device, kAudioObjectPropertyName));
    free(t);
}

// --- Taps -------------------------------------------------------------------------------------------------------------

static AudioObjectID makeAggregate(const char *kind, NSString *tapUID) {
    NSMutableDictionary *description = [@{
        @kAudioAggregateDeviceNameKey: @"HindsoundProbeTap",
        @kAudioAggregateDeviceUIDKey: [NSUUID UUID].UUIDString,
        @kAudioAggregateDeviceIsPrivateKey: @YES,
        @kAudioAggregateDeviceIsStackedKey: @NO,
        @kAudioAggregateDeviceTapAutoStartKey: @YES,
        @kAudioAggregateDeviceTapListKey: @[ @{ @kAudioSubTapDriftCompensationKey: @YES, @kAudioSubTapUIDKey: tapUID } ],
    } mutableCopy];
    NSString *main = nil;
    if (strcmp(kind, "out") == 0) {
        main = getString(getDevice(kAudioHardwarePropertyDefaultSystemOutputDevice), kAudioDevicePropertyDeviceUID);
    } else if (strcmp(kind, "null") == 0) {
        main = @"NullAudioDevice_UID";
    }
    if (main != nil) {
        description[@kAudioAggregateDeviceMainSubDeviceKey] = main;
        description[@kAudioAggregateDeviceSubDeviceListKey] = @[ @{ @kAudioSubDeviceUIDKey: main } ];
    }
    AudioObjectID device = kAudioObjectUnknown;
    begin("aggregate_create");
    OSStatus status = AudioHardwareCreateAggregateDevice((__bridge CFDictionaryRef)description, &device);
    end("aggregate_create", status);
    probe("aggregate", @"kind=%s main=%@ status=%d device=%u input_channels=%u rate=%.0f", kind, main ?: @"none",
          (int)status, device, status == noErr ? inputChannels(device) : 0, status == noErr ? nominalRate(device) : 0);
    return status == noErr ? device : kAudioObjectUnknown;
}

static void tapTest(const char *scope, const char *aggregateKind, const char *reader, pid_t pid) {
    char name[128];
    snprintf(name, sizeof name, "tap_%s_%s_%s", scope, aggregateKind, reader);
    BOOL global = strncmp(scope, "global", 6) == 0;
    Tone *tone = calloc(1, sizeof(Tone));
    AudioQueueRef toneQueue = global ? startTone(tone) : NULL;
    AudioObjectID selfObject = processObject(getpid());
    probe("self", @"pid=%d object=%u tone=%s", getpid(), selfObject, toneQueue ? "playing" : "none");

    CATapDescription *description = nil;
    if (strcmp(scope, "global") == 0) {
        description = [[CATapDescription alloc] initStereoGlobalTapButExcludeProcesses:@[]];
    } else if (strcmp(scope, "global_excl_self") == 0) {
        description = [[CATapDescription alloc] initStereoGlobalTapButExcludeProcesses:selfObject ? @[ @(selfObject) ] : @[]];
    } else {
        AudioObjectID target = processObject(pid);
        probe("target", @"pid=%d object=%u", pid, target);
        if (target != kAudioObjectUnknown) {
            description = [[CATapDescription alloc] initStereoMixdownOfProcesses:@[ @(target) ]];
        }
    }
    char key[160];
    snprintf(key, sizeof key, "%s.result", name);
    if (description == nil) {
        probe(key, @"NO_PROCESS_OBJECT pid=%d", pid);
    } else {
        description.muteBehavior = CATapUnmuted;
        AudioObjectID tap = kAudioObjectUnknown;
        begin("tap_create");
        OSStatus status = AudioHardwareCreateProcessTap(description, &tap);
        end("tap_create", status);
        if (status != noErr) {
            probe(key, @"ERROR tap_create=%d", (int)status);
        } else {
            AudioStreamBasicDescription format = { 0 };
            UInt32 size = sizeof format;
            OSStatus formatStatus = getProperty(tap, kAudioTapPropertyFormat, kAudioObjectPropertyScopeGlobal, 0, NULL, &size,
                                                &format);
            NSString *tapUID = getString(tap, kAudioTapPropertyUID);
            if ([tapUID isEqualToString:@"?"]) tapUID = description.UUID.UUIDString;
            probe("tap", @"id=%u format_status=%d rate=%.0f channels=%u uid=%@", tap, (int)formatStatus, format.mSampleRate,
                  format.mChannelsPerFrame, tapUID);
            AudioObjectID device = makeAggregate(aggregateKind, tapUID);
            if (device == kAudioObjectUnknown) {
                probe(key, @"ERROR aggregate");
            } else {
                if (strcmp(reader, "auhal") == 0) {
                    readAUHAL(name, device);
                } else if (strcmp(reader, "queue") == 0) {
                    readQueue(name, device, YES);
                } else {
                    readIOProc(name, device);
                }
                begin("aggregate_destroy");
                end("aggregate_destroy", AudioHardwareDestroyAggregateDevice(device));
            }
            begin("tap_destroy");
            end("tap_destroy", AudioHardwareDestroyProcessTap(tap));
        }
    }
    if (toneQueue != NULL) {
        probe("self_tone", @"callbacks=%llu", tone->callbacks);
        AudioQueueStop(toneQueue, true);
        AudioQueueDispose(toneQueue, true);
    }
    free(tone);
}

// --- Permissions --------------------------------------------------------------------------------------------------------

static void permissions(void) {
    typedef int (*Preflight)(CFStringRef, CFDictionaryRef);
    void *tcc = dlopen("/System/Library/PrivateFrameworks/TCC.framework/Versions/A/TCC", RTLD_NOW);
    Preflight preflight = tcc ? (Preflight)dlsym(tcc, "TCCAccessPreflight") : NULL;
    if (preflight != NULL) {
        // 0 granted, 1 denied, 2 not determined (as AudioCap reads it).
        probe("tcc_preflight", @"kTCCServiceAudioCapture=%d kTCCServiceMicrophone=%d",
              preflight(CFSTR("kTCCServiceAudioCapture"), NULL), preflight(CFSTR("kTCCServiceMicrophone"), NULL));
    } else {
        probe("tcc_preflight", @"UNAVAILABLE");
    }
    // 0 notDetermined, 1 restricted, 2 denied, 3 authorized.
    probe("av_microphone", @"%ld", (long)[AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeAudio]);
}

static int runTest(int argc, char **argv) {
    const char *test = argv[0];
    if (strcmp(test, "perm") == 0) {
        permissions();
    } else if (strcmp(test, "out_ioproc") == 0) {
        outIOProc();
    } else if (strcmp(test, "in_ioproc") == 0) {
        readIOProc("in_ioproc", getDevice(kAudioHardwarePropertyDefaultInputDevice));
    } else if (strcmp(test, "in_queue") == 0) {
        readQueue("in_queue", getDevice(kAudioHardwarePropertyDefaultInputDevice), NO);
    } else if (strcmp(test, "tap") == 0 && argc >= 4) {
        tapTest(argv[1], argv[2], argv[3], argc >= 5 ? (pid_t)atoi(argv[4]) : 0);
    } else {
        probe("usage", @"unknown test %s", test);
        return 2;
    }
    return 0;
}

int main(int argc, char **argv) {
    if (argc < 7) {
        fprintf(stderr, "usage: capture <tag> <step-limit-s> <capture-s> <sample-path> <null|main> <test> [args]\n");
        return 2;
    }
    snprintf(g_tag, sizeof g_tag, "%s", argv[1]);
    g_stepLimit = atof(argv[2]);
    g_seconds = atof(argv[3]);
    g_samplePath = argv[4];
    g_t0 = now();
    @autoreleasepool {
        probe("start", @"build=%d mark=%s pid=%d bundle=%@ hal_runloop=%s test=%s", PROBE_BUILD, kMark, getpid(),
              NSBundle.mainBundle.bundleIdentifier ?: @"none", argv[5], argv[6]);
    }
    if (strcmp(argv[5], "null") == 0) {
        // The HAL then sends its notifications on a thread of its own instead of the main run loop.
        CFRunLoopRef none = NULL;
        AudioObjectPropertyAddress runLoop = { kAudioHardwarePropertyRunLoop, kAudioObjectPropertyScopeGlobal,
                                               kAudioObjectPropertyElementMain };
        OSStatus status = AudioObjectSetPropertyData(kAudioObjectSystemObject, &runLoop, 0, NULL, sizeof none, &none);
        probe("hal_runloop_null", @"status=%d", (int)status);
    }
    int testArgc = argc - 6;
    char **testArgv = argv + 6;
    __block int rc = 0;
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        @autoreleasepool {
            rc = runTest(testArgc, testArgv);
        }
        g_finished = 1;
    });
    // The main thread runs its run loop (and so the main dispatch queue) and watches the step in progress.
    while (!g_finished) {
        CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.1, false);
        double started = g_stepStart;
        if (started > 0 && now() - started > g_stepLimit) {
            @autoreleasepool {
                probe("stuck", @"step=%s after=%.1fs", g_step, now() - started);
                if (g_samplePath[0] != '\0') {
                    char command[1200];
                    snprintf(command, sizeof command, "/usr/bin/sample %d 2 -mayDie -file '%s' > /dev/null 2>&1", getpid(),
                             g_samplePath);
                    probe("stuck_sample", @"rc=%d", system(command));
                }
            }
            fflush(stdout);
            _exit(3);
        }
    }
    @autoreleasepool {
        probe("end", @"rc=%d total_ms=%.0f", rc, (now() - g_t0) * 1000);
    }
    return rc;
}
