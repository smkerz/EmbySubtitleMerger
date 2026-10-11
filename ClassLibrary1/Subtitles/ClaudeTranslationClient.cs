using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Une réplique à traduire, identifiée par son index dans le fichier SRT
    /// </summary>
    public class TranslationItem
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// Résultat d'un lot de traduction
    /// </summary>
    public class TranslationBatchResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }

        /// <summary>Traductions indexées par Id de réplique</summary>
        public Dictionary<int, string> Translations { get; set; } = new Dictionary<int, string>();

        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }

    /// <summary>
    /// Client pour l'API Claude (Anthropic Messages API).
    /// Utilise HttpClient directement plutôt que le SDK NuGet : le plugin se déploie
    /// en un seul DLL dans le dossier plugins d'Emby, on évite les dépendances externes.
    /// </summary>
    public class ClaudeTranslationClient : IDisposable
    {
        /// <summary>Modèle utilisé par défaut</summary>
        public const string DefaultModel = "claude-opus-5";

        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string ApiVersion = "2023-06-01";
        private const int MaxTokens = 16000;
        private const int MaxRetries = 4;

        private const int TimeoutSeconds = 180;

        /// <summary>
        /// Sans cet encodeur, chaque apostrophe, accent ou balise &lt;i&gt; part en séquence \uXXXX :
        /// le modèle lit un texte dégradé et la requête coûte bien plus de tokens.
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly HttpClient _httpClient;
        private readonly string _model;

        public ClaudeTranslationClient(string apiKey, string? model = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Clé API Anthropic requise", nameof(apiKey));

            _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model!;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);
        }

        /// <summary>
        /// Consigne système, identique pour tous les lots. Elle porte un cache_control,
        /// mais sa taille est proche du minimum cacheable : le cache peut ne pas s'activer.
        /// </summary>
        private const string SystemPrompt =
            "You are a professional subtitle translator working on film and television subtitles.\n" +
            "\n" +
            "You receive JSON with four fields: source_language, target_language, context (cues already\n" +
            "translated, provided only so your wording stays consistent) and translate (the cues to translate).\n" +
            "\n" +
            "Rules you must follow exactly:\n" +
            "1. Return exactly one translation for every cue in `translate`, reusing its `id` unchanged. Never\n" +
            "   merge two cues into one, never split one cue across two, never add or drop a cue. The cue count\n" +
            "   you return must equal the cue count you were given.\n" +
            "2. Never return a translation for a cue that appeared in `context` — those are for continuity only.\n" +
            "3. Translate meaning, not words. Use natural spoken language, and match the register of the original:\n" +
            "   formality, slang, profanity, period voice, and how characters address each other. Once you have\n" +
            "   chosen a level of formality between two characters, keep it for the rest of the film.\n" +
            "4. Preserve inline markup exactly as it appears: <i>, <b>, <u>, <font ...>, and positioning tags such\n" +
            "   as {\\an8}. Keep a leading speaker dash when the original line has one.\n" +
            "5. Preserve the line structure: a cue written on two lines should normally come back on two lines,\n" +
            "   split at a natural phrase boundary. Use \\n inside the JSON string for the line break.\n" +
            "6. Subtitles are read in a couple of seconds. When two renderings are equally faithful, return the\n" +
            "   shorter one.\n" +
            "7. Leave untranslated what subtitles normally leave untranslated: proper nouns, brand names, and\n" +
            "   text that is already in the target language.\n" +
            "8. If a cue is empty, unintelligible, or is pure sound notation, return the closest conventional\n" +
            "   equivalent in the target language rather than an empty string.";

        /// <summary>
        /// Traduit un lot de répliques. `context` sert uniquement à la continuité et n'est pas retraduit.
        /// </summary>
        public async Task<TranslationBatchResult> TranslateBatchAsync(
            IReadOnlyList<TranslationItem> batch,
            IReadOnlyList<TranslationItem> context,
            string sourceLanguage,
            string targetLanguage,
            CancellationToken cancellationToken = default)
        {
            if (batch == null || batch.Count == 0)
                return new TranslationBatchResult { Success = true };

            var userPayload = JsonSerializer.Serialize(new
            {
                source_language = sourceLanguage,
                target_language = targetLanguage,
                context = context.Select(c => new { id = c.Id, text = c.Text }).ToArray(),
                translate = batch.Select(c => new { id = c.Id, text = c.Text }).ToArray()
            }, JsonOptions);

            var body = BuildRequestBody(userPayload);

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    using var response = await _httpClient.PostAsync(ApiUrl, content, cancellationToken);
                    var raw = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                        return ParseResponse(raw, batch);

                    // 429 et 5xx sont transitoires : on retente avec backoff
                    if (IsRetryable(response.StatusCode) && attempt < MaxRetries)
                    {
                        var delay = ComputeRetryDelay(response, attempt);
                        Plugin.Log($"[Translate] HTTP {(int)response.StatusCode}, retry dans {delay.TotalSeconds:0}s (essai {attempt + 1}/{MaxRetries})");
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    return new TranslationBatchResult
                    {
                        Success = false,
                        Error = DescribeApiError(response.StatusCode, raw)
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is TaskCanceledException || ex is HttpRequestException)
                {
                    // Timeout HttpClient (pas une annulation utilisateur) ou coupure réseau
                    if (attempt < MaxRetries)
                    {
                        await Task.Delay(Backoff(attempt), cancellationToken);
                        continue;
                    }

                    return new TranslationBatchResult
                    {
                        Success = false,
                        Error = ex is TaskCanceledException
                            ? "Timeout: l'API Claude n'a pas répondu à temps."
                            : $"Erreur réseau: {ex.Message}"
                    };
                }
            }

            return new TranslationBatchResult { Success = false, Error = "Échec après plusieurs tentatives." };
        }

        /// <summary>
        /// Construit le corps de la requête. La sortie structurée (json_schema) garantit
        /// que la réponse est exploitable sans parsing approximatif.
        /// </summary>
        private string BuildRequestBody(string userPayload)
        {
            var body = new
            {
                model = _model,
                max_tokens = MaxTokens,
                system = new object[]
                {
                    new
                    {
                        type = "text",
                        text = SystemPrompt,
                        cache_control = new { type = "ephemeral" }
                    }
                },
                thinking = new { type = "adaptive" },
                output_config = new
                {
                    effort = "low",
                    format = new
                    {
                        type = "json_schema",
                        schema = new
                        {
                            type = "object",
                            properties = new
                            {
                                translations = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            id = new { type = "integer" },
                                            text = new { type = "string" }
                                        },
                                        required = new[] { "id", "text" },
                                        additionalProperties = false
                                    }
                                }
                            },
                            required = new[] { "translations" },
                            additionalProperties = false
                        }
                    }
                },
                messages = new object[]
                {
                    new { role = "user", content = userPayload }
                }
            };

            return JsonSerializer.Serialize(body, JsonOptions);
        }

        /// <summary>
        /// Extrait les traductions de la réponse de l'API
        /// </summary>
        private static TranslationBatchResult ParseResponse(string raw, IReadOnlyList<TranslationItem> batch)
        {
            var result = new TranslationBatchResult();

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("input_tokens", out var it)) result.InputTokens = it.GetInt32();
                if (usage.TryGetProperty("output_tokens", out var ot)) result.OutputTokens = ot.GetInt32();
            }

            var stopReason = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;

            if (stopReason == "refusal")
            {
                var category = root.TryGetProperty("stop_details", out var sd)
                               && sd.ValueKind == JsonValueKind.Object
                               && sd.TryGetProperty("category", out var cat)
                    ? cat.GetString()
                    : null;

                result.Success = false;
                result.Error = string.IsNullOrEmpty(category)
                    ? "Le modèle a refusé de traiter ce lot."
                    : $"Le modèle a refusé de traiter ce lot (catégorie: {category}).";
                return result;
            }

            if (stopReason == "max_tokens")
            {
                result.Success = false;
                result.Error = "Réponse tronquée (max_tokens atteint). Réduisez la taille des lots.";
                return result;
            }

            // La sortie structurée garantit un bloc texte contenant du JSON valide
            string? payload = null;
            if (root.TryGetProperty("content", out var contentArray) && contentArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in contentArray.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
                        && block.TryGetProperty("text", out var txt))
                    {
                        payload = txt.GetString();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                result.Success = false;
                result.Error = "Réponse vide de l'API.";
                return result;
            }

            try
            {
                using var payloadDoc = JsonDocument.Parse(payload!);
                if (!payloadDoc.RootElement.TryGetProperty("translations", out var translations)
                    || translations.ValueKind != JsonValueKind.Array)
                {
                    result.Success = false;
                    result.Error = "Réponse mal formée: champ 'translations' absent.";
                    return result;
                }

                var expectedIds = new HashSet<int>(batch.Select(b => b.Id));

                foreach (var entry in translations.EnumerateArray())
                {
                    if (!entry.TryGetProperty("id", out var idEl) || !entry.TryGetProperty("text", out var textEl))
                        continue;

                    var id = idEl.GetInt32();
                    // On ignore tout id hors du lot : la réponse ne doit pas réécrire le contexte
                    if (!expectedIds.Contains(id))
                        continue;

                    result.Translations[id] = textEl.GetString() ?? string.Empty;
                }

                result.Success = true;
                return result;
            }
            catch (JsonException ex)
            {
                result.Success = false;
                result.Error = $"Réponse JSON invalide: {ex.Message}";
                return result;
            }
        }

        private static bool IsRetryable(HttpStatusCode status)
        {
            return status == HttpStatusCode.TooManyRequests
                   || status == HttpStatusCode.RequestTimeout
                   || (int)status >= 500;
        }

        private static TimeSpan ComputeRetryDelay(HttpResponseMessage response, int attempt)
        {
            if (response.Headers.TryGetValues("retry-after", out var values))
            {
                var first = values.FirstOrDefault();
                if (int.TryParse(first, out var seconds) && seconds > 0)
                    return TimeSpan.FromSeconds(Math.Min(seconds, 60));
            }

            return Backoff(attempt);
        }

        private static TimeSpan Backoff(int attempt)
        {
            return TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), 30));
        }

        private static string DescribeApiError(HttpStatusCode status, string raw)
        {
            string? message = null;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("error", out var err)
                    && err.TryGetProperty("message", out var msg))
                {
                    message = msg.GetString();
                }
            }
            catch { }

            return status switch
            {
                HttpStatusCode.Unauthorized => "Clé API Anthropic invalide ou absente.",
                HttpStatusCode.Forbidden => "Clé API sans accès à ce modèle.",
                HttpStatusCode.NotFound => $"Modèle introuvable. {message}",
                HttpStatusCode.TooManyRequests => "Quota Anthropic dépassé, réessayez plus tard.",
                _ => $"Erreur API {(int)status}: {message ?? raw}"
            };
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
