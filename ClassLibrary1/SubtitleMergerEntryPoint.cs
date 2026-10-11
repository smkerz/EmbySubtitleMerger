using System;
using System.Runtime.CompilerServices;
using EmbySubtitleMerger.Subtitles;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Plugins;

namespace EmbySubtitleMerger
{
    /// <summary>
    /// Point d'entrée du plugin au démarrage du serveur
    /// </summary>
    public class SubtitleMergerEntryPoint : IServerEntryPoint
    {
        private readonly IMediaEncoder _mediaEncoder;

        public SubtitleMergerEntryPoint(IMediaEncoder mediaEncoder)
        {
            _mediaEncoder = mediaEncoder;
        }

        public void Dispose()
        {
        }

        public void Run()
        {
            Console.WriteLine("[SubtitleMerger] Entry point started");

            // Si cette version d'Emby n'expose pas ces chemins, le plugin garde le ffmpeg du système
            try
            {
                UseEmbyFfmpeg();
            }
            catch (Exception ex)
            {
                Plugin.Log($"[Tools] ffmpeg d'Emby non utilisé: {ex.Message}");
            }
        }

        // Méthode séparée et non inlinée : une propriété absente d'une autre version d'Emby
        // lève l'exception à l'appel de cette méthode, là où Run peut l'attraper.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void UseEmbyFfmpeg()
        {
            // Emby ne donne que le chemin de ffmpeg ; ffprobe est cherché dans le même dossier.
            // EncoderPath est marqué obsolète mais existe aussi sur les versions plus anciennes d'Emby.
#pragma warning disable CS0618
            ExternalTools.UseEmbyFfmpeg(_mediaEncoder.EncoderPath);
#pragma warning restore CS0618
        }
    }
}
