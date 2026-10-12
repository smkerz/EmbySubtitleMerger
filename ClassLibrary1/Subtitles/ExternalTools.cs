using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Résultat de l'exécution d'un programme externe
    /// </summary>
    public class ToolResult
    {
        /// <summary>false si le programme n'a pas pu être lancé (absent, droits…)</summary>
        public bool Started { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;

        /// <summary>true si le programme a été arrêté parce qu'il dépassait le délai accordé</summary>
        public bool TimedOut { get; set; }

        public bool Success => Started && ExitCode == 0;

        /// <summary>Message d'erreur court pour l'affichage</summary>
        public string Summary
        {
            get
            {
                var text = (Error + " " + Output).Trim();
                return text.Length > 200 ? text.Substring(0, 200) : text;
            }
        }
    }

    /// <summary>
    /// Programmes externes utilisés par le plugin : ffmpeg, ffprobe et mkvpropedit
    /// </summary>
    public static class ExternalTools
    {
        /// <summary>
        /// ffmpeg doit lire toute la vidéo pour en sortir une piste : sur un NAS,
        /// un film 4K de plusieurs dizaines de Go prend plusieurs minutes.
        /// </summary>
        public const int ConversionTimeoutSeconds = 1800;

        /// <summary>Programmes qui ont déjà répondu : inutile de les relancer à chaque affichage de la page</summary>
        private static readonly ConcurrentDictionary<string, bool> Responding = new ConcurrentDictionary<string, bool>();

        /// <summary>
        /// Lance un programme et attend sa fin. Les arguments sont passés un par un :
        /// un nom de fichier contenant des espaces ou des guillemets ne casse pas la commande.
        /// </summary>
        public static async Task<ToolResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            int timeoutSeconds = 120,
            CancellationToken cancellationToken = default)
        {
            var result = new ToolResult();

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.Start();
                result.Started = true;

                // Les deux flux sont lus en parallèle : un tampon plein bloquerait le programme
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch { }
                    cancellationToken.ThrowIfCancellationRequested();

                    result.ExitCode = -1;
                    result.TimedOut = true;
                    result.Error = $"Délai dépassé ({timeoutSeconds} s)";
                    return result;
                }

                result.Output = await outputTask;
                result.Error = await errorTask;
                result.ExitCode = process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Convertit une piste d'une vidéo (ou un fichier de sous-titres si streamIndex est null)
        /// en .srt débarrassé de ses codes de style.
        /// </summary>
        public static Task<ToolResult> ConvertToSrtAsync(
            string input, int? streamIndex, string output, CancellationToken cancellationToken = default)
        {
            return ConvertToSrtAsync(input, new[] { (streamIndex, output) }, cancellationToken);
        }

        /// <summary>
        /// Convertit plusieurs pistes d'une même vidéo en un seul passage de ffmpeg.
        /// Le résultat n'est un succès que si tous les .srt ont été obtenus ; sinon aucun n'est écrit,
        /// et les fichiers qui existaient déjà sous ces noms restent tels qu'ils étaient.
        /// </summary>
        public static async Task<ToolResult> ConvertToSrtAsync(
            string input,
            IReadOnlyList<(int? StreamIndex, string Output)> outputs,
            CancellationToken cancellationToken = default)
        {
            // ffmpeg écrit dans des fichiers provisoires, renommés seulement une fois complets et nettoyés.
            // Une conversion annulée ou en échec ne laisse donc ni .srt tronqué, ni .srt existant abîmé.
            var pending = outputs.Select(o => (o.StreamIndex, o.Output, Temp: o.Output + ".partiel")).ToList();

            var arguments = new List<string> { "-y", "-v", "error", "-i", input };
            foreach (var (streamIndex, _, temp) in pending)
            {
                if (streamIndex.HasValue)
                {
                    arguments.Add("-map");
                    arguments.Add($"0:{streamIndex.Value}");
                }
                arguments.AddRange(new[] { "-c:s", "srt", "-f", "srt", temp });
            }

            try
            {
                var result = await RunAsync(FfmpegHelper.FfmpegPath, arguments, ConversionTimeoutSeconds, cancellationToken);

                if (result.Success && pending.Any(p => !File.Exists(p.Temp) || new FileInfo(p.Temp).Length == 0))
                    result.ExitCode = -1;

                if (result.Success)
                {
                    try
                    {
                        foreach (var (_, output, temp) in pending)
                        {
                            var text = File.ReadAllText(temp, Encoding.UTF8);
                            File.WriteAllText(temp, NormalizeNaming.CleanSrtText(text), new UTF8Encoding(false));
                        }

                        foreach (var (_, output, temp) in pending)
                            File.Move(temp, output, true);
                    }
                    catch (Exception ex)
                    {
                        result.ExitCode = -1;
                        result.Error = ex.Message;
                    }
                }

                if (!result.Success && result.Started && string.IsNullOrWhiteSpace(result.Summary))
                    result.Error = "aucun texte obtenu. Le sous-titre est peut-être au format image (PGS/VobSub).";

                return result;
            }
            finally
            {
                foreach (var (_, _, temp) in pending)
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                }
            }
        }

        /// <summary>
        /// Indique si un programme répond à "-version". Une réponse positive est mémorisée ;
        /// une absence est revérifiée à chaque fois, pour qu'une installation soit vue sans redémarrer Emby.
        /// </summary>
        public static async Task<bool> RespondsAsync(string fileName)
        {
            if (Responding.ContainsKey(fileName)) return true;

            var result = await RunAsync(fileName, new[] { "-version" }, 15);
            if (result.Success) Responding[fileName] = true;
            return result.Success;
        }

        /// <summary>
        /// Utilise le ffmpeg livré avec Emby, et le ffprobe posé à côté, plutôt que ceux
        /// du système (souvent absents ou anciens sur un NAS).
        /// </summary>
        public static void UseEmbyFfmpeg(string? encoderPath)
        {
            if (!string.IsNullOrEmpty(encoderPath) && File.Exists(encoderPath))
            {
                FfmpegHelper.FfmpegPath = encoderPath!;

                var probePath = Path.Combine(Path.GetDirectoryName(encoderPath) ?? string.Empty, "ffprobe" + Path.GetExtension(encoderPath));
                if (File.Exists(probePath))
                    FfmpegHelper.FfprobePath = probePath;
            }

            Plugin.Log($"[Tools] ffmpeg={FfmpegHelper.FfmpegPath} ffprobe={FfmpegHelper.FfprobePath}");
        }

        /// <summary>
        /// Chemin de mkvpropedit (MKVToolNix), ou null s'il n'est pas installé.
        /// Cherché à chaque appel : l'installer suffit, sans redémarrer Emby.
        /// </summary>
        public static string? FindMkvPropEdit()
        {
            var names = OperatingSystem.IsWindows()
                ? new[] { "mkvpropedit.exe" }
                : new[] { "mkvpropedit" };

            var directories = new List<string>();

            var path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
                directories.AddRange(path.Split(Path.PathSeparator));

            if (OperatingSystem.IsWindows())
            {
                foreach (var variable in new[] { "ProgramFiles", "ProgramW6432", "ProgramFiles(x86)" })
                {
                    var root = Environment.GetEnvironmentVariable(variable);
                    if (!string.IsNullOrEmpty(root))
                        directories.Add(Path.Combine(root, "MKVToolNix"));
                }
            }
            else
            {
                directories.AddRange(new[]
                {
                    "/usr/local/bin",
                    "/usr/bin",
                    "/opt/bin",
                    "/opt/homebrew/bin",
                    // Paquet SynoCommunity sur Synology
                    "/var/packages/mkvtoolnix/target/bin",
                    "/usr/local/mkvtoolnix/bin"
                });

                try
                {
                    directories.AddRange(Directory.GetDirectories("/", "volume*")
                        .Select(volume => Path.Combine(volume, "@appstore", "mkvtoolnix", "bin")));
                }
                catch { }
            }

            foreach (var directory in directories.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct())
            {
                foreach (var name in names)
                {
                    try
                    {
                        var candidate = Path.Combine(directory.Trim(), name);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { }
                }
            }

            return null;
        }
    }
}
