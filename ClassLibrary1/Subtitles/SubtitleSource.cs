using System;
using System.IO;
using System.Threading.Tasks;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Un sous-titre prêt à être lu en SRT : fichier externe utilisé tel quel,
    /// ou piste embarquée extraite dans un fichier temporaire.
    /// </summary>
    public class SubtitleSource
    {
        public string Path { get; private set; } = string.Empty;

        /// <summary>true si le fichier a été extrait par le plugin et doit être supprimé après usage</summary>
        public bool IsTemp { get; private set; }

        /// <summary>Renseigné quand le sous-titre n'a pas pu être obtenu</summary>
        public string? Error { get; private set; }

        public bool Success => Error == null;

        /// <summary>
        /// Obtient le SRT d'une piste : le fichier externe s'il est fourni, sinon extraction avec FFmpeg.
        /// </summary>
        /// <param name="label">Nom de la piste dans les messages d'erreur (ex: "sous-titre 1")</param>
        public static async Task<SubtitleSource> AcquireAsync(
            string videoPath,
            bool isExternal,
            string? externalPath,
            int streamIndex,
            string label)
        {
            if (isExternal && !string.IsNullOrEmpty(externalPath))
            {
                return File.Exists(externalPath)
                    ? new SubtitleSource { Path = externalPath! }
                    : new SubtitleSource { Error = $"Fichier {label} non trouvé: {externalPath}" };
            }

            if (!await FfmpegHelper.IsAvailableAsync())
            {
                return new SubtitleSource
                {
                    Error = "FFmpeg n'est pas disponible. Installez FFmpeg et assurez-vous qu'il est dans le PATH."
                };
            }

            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EmbySubtitleMerger");
            Directory.CreateDirectory(tempDir);

            // Nom unique : deux opérations simultanées sur la même vidéo ne se marchent pas dessus
            var tempPath = System.IO.Path.Combine(tempDir, $"{Guid.NewGuid():N}.srt");

            var extraction = await FfmpegHelper.ExtractSubtitleByAbsoluteIndexAsync(videoPath, streamIndex, tempPath);
            if (!extraction.Success)
            {
                DeleteQuietly(tempPath);
                return new SubtitleSource { Error = $"Erreur extraction {label}: {extraction.Error}" };
            }

            return new SubtitleSource { Path = tempPath, IsTemp = true };
        }

        /// <summary>
        /// Supprime le fichier s'il est temporaire. Un fichier externe n'est jamais touché.
        /// </summary>
        public void Cleanup()
        {
            if (IsTemp) DeleteQuietly(Path);
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }
    }
}
