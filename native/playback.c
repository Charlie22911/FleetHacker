#include <string.h>

int mcr_ffmpeg_main(int argc, char **argv);

/* Run once in the isolated playback worker. All options except the two
   validated paths and the selected local demuxer are fixed. */
__declspec(dllexport) int mcr_playback(char *format, char *source, char *destination) {
    if (!format || !source || !destination) return 1;
    char *args[] = {
        "mcr-playback", "-nostdin", "-nostats", "-loglevel", "quiet", "-n",
        "-protocol_whitelist", "file", "-threads", "2", "-max_pixels", "32000000",
        "-f", format,
        "-i", source,
        "-map", "0:v:0", "-map", "0:a:0?", "-map_metadata", "-1",
        "-filter_threads", "2",
        "-vf", "scale=w='max(2,min(1280,iw))':h='max(2,min(720,ih))':force_original_aspect_ratio=decrease:force_divisible_by=2,setsar=1",
        "-pix_fmt", "yuv420p", "-c:v", "wmv2", "-b:v", "1600k", "-g", "15",
        "-c:a", "wmav2", "-b:a", "128k", "-ac", "2", "-ar", "44100",
        "-fs", "268435456", "-f", "asf", destination, NULL
    };
    if (strcmp(format, "flv") && strcmp(format, "gif") && strcmp(format, "matroska")) return 1;
    return mcr_ffmpeg_main((int)(sizeof(args) / sizeof(args[0])) - 1, args);
}
