namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Emplacement de ffmpeg et ffprobe. Par défaut ceux du système ; au démarrage,
    /// ExternalTools.UseEmbyFfmpeg les remplace par ceux livrés avec Emby.
    /// </summary>
    public static class FfmpegHelper
    {
        public static string FfmpegPath { get; set; } = "ffmpeg";

        public static string FfprobePath { get; set; } = "ffprobe";
    }
}
