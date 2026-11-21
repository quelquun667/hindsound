// Runs the audio part of the macOS probe from a plain C program, without .NET and outside any bundle: the runner is then
// the process TCC asks about. Usage: probe_audio_cli <tag> <tone-pid> <silent-pid> <seconds>
#include <stdlib.h>

int probe_audio_run(const char *tag, int tonePid, int silentPid, double seconds);

int main(int argc, char **argv) {
    if (argc < 5) return 2;
    return probe_audio_run(argv[1], atoi(argv[2]), atoi(argv[3]), atof(argv[4]));
}
