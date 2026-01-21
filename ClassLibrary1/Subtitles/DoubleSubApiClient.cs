using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Response from DoubleSub.io API
    /// </summary>
    public class DoubleSubApiResponse
    {
        public bool Success { get; set; }
        public int CueCount { get; set; }
        public string? DownloadUrl { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Client pour l'API DoubleSub.io
    /// Permet de fusionner des sous-titres via le service cloud
    /// </summary>
    public class DoubleSubApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string? _apiKey;
        private bool _disposed;

        /// <summary>
        /// URL par défaut de l'API DoubleSub.io
        /// </summary>
        public const string DefaultApiUrl = "https://doublesub.io";

        /// <summary>
        /// Crée un nouveau client API
        /// </summary>
        /// <param name="apiKey">Clé API DoubleSub.io (requise pour /api/v1/merge)</param>
        /// <param name="baseUrl">URL de base (par défaut: https://doublesub.io)</param>
        /// <param name="timeoutSeconds">Timeout en secondes (par défaut: 60)</param>
        public DoubleSubApiClient(string? apiKey = null, string? baseUrl = null, int timeoutSeconds = 60)
        {
            _baseUrl = baseUrl ?? DefaultApiUrl;
            _apiKey = apiKey;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            // Ajouter la clé API dans les headers si fournie
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            }
        }

        /// <summary>
        /// Fusionne deux fichiers SRT via l'API DoubleSub.io
        /// </summary>
        /// <param name="srt1Path">Chemin du premier fichier SRT (affiché en haut)</param>
        /// <param name="srt2Path">Chemin du second fichier SRT (affiché en bas)</param>
        /// <param name="outputPath">Chemin de sortie pour le fichier fusionné</param>
        /// <param name="mode">Mode de fusion: all, overlapping, primary</param>
        /// <param name="toleranceMs">Tolérance en ms</param>
        /// <param name="color1">Couleur HTML pour le premier sous-titre (ex: #FFFFFF)</param>
        /// <param name="color2">Couleur HTML pour le second sous-titre (ex: #FFFF00)</param>
        /// <returns>Résultat de la fusion</returns>
        public async Task<MergeResult> MergeAsync(
            string srt1Path,
            string srt2Path,
            string outputPath,
            string mode = "all",
            int toleranceMs = 700,
            string? color1 = null,
            string? color2 = null)
        {
            try
            {
                // Vérifier que les fichiers existent
                if (!File.Exists(srt1Path))
                    return MergeResult.Failure($"Fichier non trouvé: {srt1Path}");

                if (!File.Exists(srt2Path))
                    return MergeResult.Failure($"Fichier non trouvé: {srt2Path}");

                // Préparer la requête multipart
                using var form = new MultipartFormDataContent();

                // Ajouter les fichiers
                var srt1Bytes = await File.ReadAllBytesAsync(srt1Path);
                var srt2Bytes = await File.ReadAllBytesAsync(srt2Path);

                form.Add(new ByteArrayContent(srt1Bytes), "srt1", Path.GetFileName(srt1Path));
                form.Add(new ByteArrayContent(srt2Bytes), "srt2", Path.GetFileName(srt2Path));

                // Ajouter les paramètres
                form.Add(new StringContent(mode), "mode");
                form.Add(new StringContent(toleranceMs.ToString()), "tolerance");

                if (!string.IsNullOrEmpty(color1))
                    form.Add(new StringContent(color1), "color1");

                if (!string.IsNullOrEmpty(color2))
                    form.Add(new StringContent(color2), "color2");

                // Envoyer la requête (format=file pour recevoir le SRT directement)
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/merge", form);

                if (!response.IsSuccessStatusCode)
                {
                    // Essayer de lire l'erreur JSON
                    var errorContent = await response.Content.ReadAsStringAsync();
                    try
                    {
                        var errorJson = JsonSerializer.Deserialize<DoubleSubApiResponse>(errorContent,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        return MergeResult.Failure(errorJson?.Error ?? $"Erreur HTTP {(int)response.StatusCode}");
                    }
                    catch
                    {
                        return MergeResult.Failure($"Erreur HTTP {(int)response.StatusCode}: {errorContent}");
                    }
                }

                // Sauvegarder le fichier SRT reçu
                var srtContent = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(outputPath, srtContent);

                // Compter les cues dans le fichier résultat
                var cueCount = CountCuesInFile(outputPath);

                return MergeResult.Ok(cueCount, outputPath);
            }
            catch (TaskCanceledException)
            {
                return MergeResult.Failure("Timeout: le serveur DoubleSub.io n'a pas répondu à temps");
            }
            catch (HttpRequestException ex)
            {
                return MergeResult.Failure($"Erreur réseau: {ex.Message}");
            }
            catch (Exception ex)
            {
                return MergeResult.Failure($"Erreur inattendue: {ex.Message}");
            }
        }

        /// <summary>
        /// Fusionne deux fichiers SRT et retourne le contenu JSON avec preview
        /// </summary>
        public async Task<DoubleSubApiResponse> MergeWithPreviewAsync(
            string srt1Path,
            string srt2Path,
            string mode = "all",
            int toleranceMs = 700,
            string? color1 = null,
            string? color2 = null)
        {
            try
            {
                if (!File.Exists(srt1Path) || !File.Exists(srt2Path))
                {
                    return new DoubleSubApiResponse
                    {
                        Success = false,
                        Error = "Fichiers SRT non trouvés"
                    };
                }

                using var form = new MultipartFormDataContent();

                var srt1Bytes = await File.ReadAllBytesAsync(srt1Path);
                var srt2Bytes = await File.ReadAllBytesAsync(srt2Path);

                form.Add(new ByteArrayContent(srt1Bytes), "srt1", Path.GetFileName(srt1Path));
                form.Add(new ByteArrayContent(srt2Bytes), "srt2", Path.GetFileName(srt2Path));
                form.Add(new StringContent(mode), "mode");
                form.Add(new StringContent(toleranceMs.ToString()), "tolerance");
                form.Add(new StringContent("json"), "format"); // Demander JSON avec preview

                if (!string.IsNullOrEmpty(color1))
                    form.Add(new StringContent(color1), "color1");

                if (!string.IsNullOrEmpty(color2))
                    form.Add(new StringContent(color2), "color2");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/merge", form);
                var content = await response.Content.ReadAsStringAsync();

                return JsonSerializer.Deserialize<DoubleSubApiResponse>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new DoubleSubApiResponse { Success = false, Error = "Réponse invalide" };
            }
            catch (Exception ex)
            {
                return new DoubleSubApiResponse
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// Vérifie si l'API DoubleSub.io est accessible
        /// </summary>
        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Compte le nombre de cues dans un fichier SRT
        /// </summary>
        private static int CountCuesInFile(string path)
        {
            try
            {
                var content = File.ReadAllText(path);
                var lines = content.Split('\n');
                int count = 0;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (int.TryParse(trimmed, out _))
                    {
                        count++;
                    }
                }

                return count;
            }
            catch
            {
                return 0;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Résultat d'une fusion de sous-titres
    /// </summary>
    public class MergeResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int CueCount { get; set; }
        public string? OutputPath { get; set; }

        public static MergeResult Ok(int cueCount, string outputPath)
        {
            return new MergeResult
            {
                Success = true,
                CueCount = cueCount,
                OutputPath = outputPath
            };
        }

        public static MergeResult Failure(string error)
        {
            return new MergeResult
            {
                Success = false,
                Error = error
            };
        }
    }
}
