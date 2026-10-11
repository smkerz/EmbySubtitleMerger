using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Un sous-titre prêt à être lu en SRT. Un fichier .srt externe est utilisé tel quel ;
    /// toute autre piste (intégrée à la vidéo, ou fichier .ass/.ssa/.vtt) est convertie en SRT
    /// dans un fichier temporaire et débarrassée de ses codes de style, comme le fait la normalisation.
    /// </summary>
    public class SubtitleSource
    {
        public string Path { get; private set; } = string.Empty;

        /// <summary>true si le fichier a été créé par le plugin et doit être supprimé après usage</summary>
        public bool IsTemp { get; private set; }

        /// <summary>Renseigné quand le sous-titre n'a pas pu être obtenu</summary>
        public string? Error { get; private set; }

        public bool Success => Error == null;

        /// <summary>
        /// Obtient le SRT d'une piste.
        /// </summary>
        /// <param name="label">Nom de la piste dans les messages d'erreur (ex: "sous-titre 1")</param>
        public static async Task<SubtitleSource> AcquireAsync(
            string videoPath,
            bool isExternal,
            string? externalPath,
            int streamIndex,
            string label,
            CancellationToken cancellationToken = default)
        {
            var external = isExternal && !string.IsNullOrEmpty(externalPath);

            if (external && !File.Exists(externalPath))
                return new SubtitleSource { Error = $"Fichier {label} non trouvé: {externalPath}" };

            if (external && NormalizeNaming.IsSrt(externalPath!))
                return new SubtitleSource { Path = externalPath! };

            var tempPath = NewTempPath();
            var result = await ExternalTools.ConvertToSrtAsync(
                external ? externalPath! : videoPath, external ? (int?)null : streamIndex, tempPath, cancellationToken);

            if (!result.Started)
            {
                return new SubtitleSource
                {
                    Error = $"FFmpeg n'a pas pu être lancé ({FfmpegHelper.FfmpegPath}) : {result.Summary}"
                };
            }

            if (!result.Success)
                return new SubtitleSource { Error = $"Erreur extraction {label}: {result.Summary}" };

            return new SubtitleSource { Path = tempPath, IsTemp = true };
        }

        /// <summary>
        /// Obtient les SRT des deux pistes d'une fusion. Quand elles sont toutes deux intégrées à la vidéo,
        /// un seul passage de ffmpeg les sort ensemble : la vidéo n'est lue qu'une fois au lieu de deux.
        /// </summary>
        public static async Task<(SubtitleSource First, SubtitleSource Second)> AcquirePairAsync(
            string videoPath,
            bool firstIsExternal, string? firstPath, int firstIndex,
            bool secondIsExternal, string? secondPath, int secondIndex,
            CancellationToken cancellationToken = default)
        {
            var bothEmbedded = !(firstIsExternal && !string.IsNullOrEmpty(firstPath))
                && !(secondIsExternal && !string.IsNullOrEmpty(secondPath))
                && firstIndex != secondIndex;

            if (bothEmbedded)
            {
                var firstTemp = NewTempPath();
                var secondTemp = NewTempPath();

                var result = await ExternalTools.ConvertToSrtAsync(
                    videoPath, new[] { ((int?)firstIndex, firstTemp), ((int?)secondIndex, secondTemp) }, cancellationToken);

                if (result.Success)
                {
                    return (new SubtitleSource { Path = firstTemp, IsTemp = true },
                            new SubtitleSource { Path = secondTemp, IsTemp = true });
                }

                // Une piste illisible fait échouer tout le passage : on recommence piste par piste
                // pour savoir laquelle est en cause
            }

            var first = await AcquireAsync(videoPath, firstIsExternal, firstPath, firstIndex, "sous-titre 1", cancellationToken);
            if (!first.Success)
                return (first, new SubtitleSource { Error = first.Error });

            var second = await AcquireAsync(videoPath, secondIsExternal, secondPath, secondIndex, "sous-titre 2", cancellationToken);
            if (!second.Success)
                first.Cleanup();

            return (first, second);
        }

        /// <summary>
        /// Nom unique : deux opérations simultanées sur la même vidéo ne se marchent pas dessus
        /// </summary>
        private static string NewTempPath()
        {
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EmbySubtitleMerger");
            Directory.CreateDirectory(tempDir);
            return System.IO.Path.Combine(tempDir, $"{Guid.NewGuid():N}.srt");
        }

        /// <summary>
        /// Supprime le fichier s'il est temporaire. Un fichier externe n'est jamais touché.
        /// </summary>
        public void Cleanup()
        {
            if (!IsTemp) return;

            try
            {
                if (File.Exists(Path))
                    File.Delete(Path);
            }
            catch { }
        }
    }
}
