using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Résultat de l'extraction d'un sous-titre
    /// </summary>
    public class ExtractionResult
    {
        public bool Success { get; set; }
        public string? OutputPath { get; set; }
        public string? Error { get; set; }
        public string? StandardOutput { get; set; }
        public string? StandardError { get; set; }
    }
    
    /// <summary>
    /// Helper pour les opérations FFmpeg
    /// </summary>
    public static class FfmpegHelper
    {
        /// <summary>
        /// Chemin vers ffmpeg.exe (peut être configuré)
        /// </summary>
        public static string FfmpegPath { get; set; } = "ffmpeg";
        
        /// <summary>
        /// Chemin vers ffprobe.exe (peut être configuré)
        /// </summary>
        public static string FfprobePath { get; set; } = "ffprobe";
        
        /// <summary>
        /// Extrait un sous-titre d'un fichier vidéo
        /// </summary>
        /// <param name="videoPath">Chemin du fichier vidéo</param>
        /// <param name="streamIndex">Index du stream de sous-titre (0-based dans les sous-titres)</param>
        /// <param name="outputPath">Chemin de sortie (optionnel, généré si null)</param>
        /// <param name="cancellationToken">Token d'annulation</param>
        /// <returns>Résultat de l'extraction</returns>
        public static async Task<ExtractionResult> ExtractSubtitleAsync(
            string videoPath, 
            int streamIndex, 
            string? outputPath = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Générer le chemin de sortie si non spécifié
                if (string.IsNullOrEmpty(outputPath))
                {
                    var dir = Path.GetDirectoryName(videoPath) ?? ".";
                    var name = Path.GetFileNameWithoutExtension(videoPath);
                    outputPath = Path.Combine(dir, $"{name}.stream{streamIndex}.srt");
                }
                
                // Supprimer le fichier de sortie s'il existe
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
                
                // Construire la commande FFmpeg
                // -map 0:s:X sélectionne le Xème sous-titre (0-based)
                var arguments = $"-i \"{videoPath}\" -map 0:s:{streamIndex} -c:s srt \"{outputPath}\" -y";
                
                var result = await RunFfmpegAsync(arguments, cancellationToken);
                
                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                {
                    result.Success = true;
                    result.OutputPath = outputPath;
                }
                else
                {
                    result.Success = false;
                    result.Error = "Le fichier de sortie n'a pas été créé ou est vide";
                }
                
                return result;
            }
            catch (Exception ex)
            {
                return new ExtractionResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }
        
        /// <summary>
        /// Extrait un sous-titre par son index absolu dans le fichier
        /// </summary>
        /// <param name="videoPath">Chemin du fichier vidéo</param>
        /// <param name="absoluteStreamIndex">Index absolu du stream (comme dans MediaStreams)</param>
        /// <param name="outputPath">Chemin de sortie</param>
        /// <param name="cancellationToken">Token d'annulation</param>
        public static async Task<ExtractionResult> ExtractSubtitleByAbsoluteIndexAsync(
            string videoPath, 
            int absoluteStreamIndex, 
            string? outputPath = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrEmpty(outputPath))
                {
                    var dir = Path.GetDirectoryName(videoPath) ?? ".";
                    var name = Path.GetFileNameWithoutExtension(videoPath);
                    outputPath = Path.Combine(dir, $"{name}.idx{absoluteStreamIndex}.srt");
                }
                
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
                
                // Utiliser l'index absolu avec 0:X
                var arguments = $"-i \"{videoPath}\" -map 0:{absoluteStreamIndex} -c:s srt \"{outputPath}\" -y";
                
                var result = await RunFfmpegAsync(arguments, cancellationToken);
                
                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                {
                    result.Success = true;
                    result.OutputPath = outputPath;
                }
                else
                {
                    result.Success = false;
                    result.Error = "Le fichier de sortie n'a pas été créé ou est vide. Le sous-titre est peut-être au format image (PGS/VobSub).";
                }
                
                return result;
            }
            catch (Exception ex)
            {
                return new ExtractionResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }
        
        /// <summary>
        /// Exécute une commande FFmpeg
        /// </summary>
        private static async Task<ExtractionResult> RunFfmpegAsync(
            string arguments, 
            CancellationToken cancellationToken)
        {
            var result = new ExtractionResult();
            
            var startInfo = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            
            using var process = new Process { StartInfo = startInfo };
            
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            
            process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            
            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                // Attendre la fin avec timeout
                var completed = await Task.Run(() => process.WaitForExit(60000), cancellationToken);
                
                if (!completed)
                {
                    process.Kill();
                    result.Error = "Timeout: FFmpeg n'a pas terminé dans les 60 secondes";
                    return result;
                }
                
                result.StandardOutput = stdout.ToString();
                result.StandardError = stderr.ToString();
                
                if (process.ExitCode != 0)
                {
                    result.Error = $"FFmpeg a retourné le code {process.ExitCode}";
                }
            }
            catch (Exception ex)
            {
                result.Error = $"Erreur lors de l'exécution de FFmpeg: {ex.Message}";
            }
            
            return result;
        }
        
        /// <summary>
        /// Vérifie si FFmpeg est disponible
        /// </summary>
        public static async Task<bool> IsAvailableAsync()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = FfmpegPath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(startInfo);
                if (process == null)
                    return false;
                
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Obtient la version de FFmpeg
        /// </summary>
        public static async Task<string?> GetVersionAsync()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = FfmpegPath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(startInfo);
                if (process == null)
                    return null;
                
                var output = await process.StandardOutput.ReadLineAsync();
                await process.WaitForExitAsync();
                
                return output;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Convertit un sous-titre PGS/VobSub en SRT via OCR (nécessite Tesseract)
        /// Note: Cette fonctionnalité nécessite des outils supplémentaires
        /// </summary>
        public static async Task<ExtractionResult> ConvertImageSubtitleToSrtAsync(
            string videoPath,
            int streamIndex,
            string? outputPath = null,
            string language = "eng",
            CancellationToken cancellationToken = default)
        {
            // Pour les sous-titres image (PGS, VobSub), on aurait besoin de:
            // 1. Extraire en format SUP/SUB
            // 2. Utiliser un outil OCR comme Tesseract ou SubtitleEdit
            // C'est plus complexe et nécessite des dépendances supplémentaires
            
            return new ExtractionResult
            {
                Success = false,
                Error = "La conversion de sous-titres image (PGS/VobSub) en SRT nécessite un outil OCR externe. " +
                       "Utilisez SubtitleEdit ou un outil similaire pour convertir manuellement."
            };
        }
    }
}


