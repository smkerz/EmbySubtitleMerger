using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// État d'un job de traduction
    /// </summary>
    public enum TranslationJobStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Job de traduction suivi en mémoire.
    /// Une traduction complète prend plusieurs minutes : on ne peut pas la faire
    /// tenir dans une requête HTTP synchrone, d'où ce suivi par polling.
    /// </summary>
    public class TranslationJob
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public TranslationJobStatus Status { get; set; } = TranslationJobStatus.Pending;

        public int Processed { get; set; }
        public int Total { get; set; }

        /// <summary>Répliques que le modèle n'a pas renvoyées et qui gardent le texte d'origine</summary>
        public int Untranslated { get; set; }

        public string? Message { get; set; }
        public string? Error { get; set; }

        /// <summary>Chemin du SRT traduit</summary>
        public string? OutputPath { get; set; }

        /// <summary>Chemin du SRT bilingue, si une fusion a suivi la traduction</summary>
        public string? MergedPath { get; set; }

        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }

        public DateTime? FinishedAt { get; set; }

        internal CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

        public int PercentComplete => Total <= 0 ? 0 : (int)Math.Round(100.0 * Processed / Total);
    }

    /// <summary>
    /// Stockage en mémoire des jobs de traduction en cours et récents
    /// </summary>
    public static class TranslationJobStore
    {
        private static readonly ConcurrentDictionary<string, TranslationJob> Jobs =
            new ConcurrentDictionary<string, TranslationJob>();

        private static readonly TimeSpan RetentionPeriod = TimeSpan.FromHours(1);

        public static TranslationJob Create()
        {
            PurgeExpired();
            var job = new TranslationJob();
            Jobs[job.Id] = job;
            return job;
        }

        public static TranslationJob? Get(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Jobs.TryGetValue(id!, out var job) ? job : null;
        }

        public static bool Cancel(string? id)
        {
            var job = Get(id);
            if (job == null) return false;

            if (job.Status == TranslationJobStatus.Running || job.Status == TranslationJobStatus.Pending)
            {
                job.Cancellation.Cancel();
                return true;
            }
            return false;
        }

        /// <summary>Retire les jobs terminés depuis plus d'une heure</summary>
        private static void PurgeExpired()
        {
            var cutoff = DateTime.UtcNow - RetentionPeriod;
            foreach (var kvp in Jobs)
            {
                var finished = kvp.Value.FinishedAt;
                if (finished.HasValue && finished.Value < cutoff)
                    Jobs.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// Traduit un fichier SRT complet via l'API Claude.
    ///
    /// Le point délicat est l'alignement : une traduction naïve fusionne ou découpe
    /// les répliques et fait perdre le timing. On envoie donc les répliques numérotées,
    /// on impose une sortie structurée, et on vérifie l'alignement lot par lot.
    /// </summary>
    public static class SubtitleTranslator
    {
        /// <summary>Répliques envoyées par appel API</summary>
        public const int DefaultBatchSize = 40;

        /// <summary>Répliques déjà traduites données en contexte pour la continuité</summary>
        private const int ContextSize = 3;

        /// <summary>
        /// Traduit une liste de répliques. Les timings sont conservés à l'identique :
        /// seuls les textes changent.
        /// </summary>
        public static async Task<List<SubtitleCue>> TranslateCuesAsync(
            IReadOnlyList<SubtitleCue> cues,
            string? sourceLanguage,
            string targetLanguage,
            string apiKey,
            string? model = null,
            int batchSize = DefaultBatchSize,
            TranslationJob? job = null,
            CancellationToken cancellationToken = default)
        {
            if (cues == null) throw new ArgumentNullException(nameof(cues));

            var sourceName = LanguageName(sourceLanguage);
            var targetName = LanguageName(targetLanguage);

            // Copie profonde : on ne modifie jamais les cues d'origine
            var translated = cues.Select(c => new SubtitleCue
            {
                Index = c.Index,
                StartTime = c.StartTime,
                EndTime = c.EndTime,
                Text = c.Text
            }).ToList();

            if (translated.Count == 0)
                return translated;

            using var client = new ClaudeTranslationClient(apiKey, model);

            // Id = position dans la liste, stable et indépendant de l'index SRT d'origine
            var items = translated
                .Select((c, i) => new TranslationItem { Id = i, Text = c.Text })
                .ToList();

            var previousBatchTranslations = new List<TranslationItem>();

            for (int offset = 0; offset < items.Count; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = items.Skip(offset).Take(batchSize).ToList();
                var context = previousBatchTranslations
                    .Skip(Math.Max(0, previousBatchTranslations.Count - ContextSize))
                    .ToList();

                var result = await client.TranslateBatchAsync(
                    batch, context, sourceName, targetName, cancellationToken);

                if (!result.Success)
                    throw new InvalidOperationException(result.Error ?? "Échec de la traduction du lot.");

                if (job != null)
                {
                    job.InputTokens += result.InputTokens;
                    job.OutputTokens += result.OutputTokens;
                }

                // Vérification d'alignement : toute réplique manquante est redemandée une fois
                var missing = batch.Where(b => !result.Translations.ContainsKey(b.Id)).ToList();
                if (missing.Count > 0)
                {
                    Plugin.Log($"[Translate] {missing.Count} réplique(s) manquante(s) sur le lot {offset}-{offset + batch.Count - 1}, nouvelle tentative");

                    var retry = await client.TranslateBatchAsync(
                        missing, context, sourceName, targetName, cancellationToken);

                    if (retry.Success)
                    {
                        foreach (var kvp in retry.Translations)
                            result.Translations[kvp.Key] = kvp.Value;

                        if (job != null)
                        {
                            job.InputTokens += retry.InputTokens;
                            job.OutputTokens += retry.OutputTokens;
                        }
                    }
                }

                var appliedContext = new List<TranslationItem>();
                foreach (var item in batch)
                {
                    if (result.Translations.TryGetValue(item.Id, out var text) && !string.IsNullOrWhiteSpace(text))
                    {
                        translated[item.Id].Text = text;
                        appliedContext.Add(new TranslationItem { Id = item.Id, Text = text });
                    }
                    else
                    {
                        // Le texte d'origine est conservé : mieux vaut une réplique non traduite
                        // qu'un trou ou qu'un décalage de timing
                        if (job != null) job.Untranslated++;
                        Plugin.Log($"[Translate] Réplique {item.Id} non traduite, texte d'origine conservé");
                    }
                }

                previousBatchTranslations = appliedContext;

                if (job != null)
                {
                    job.Processed = offset + batch.Count;
                    job.Message = $"Traduction en cours… {job.Processed}/{job.Total} répliques";
                }
            }

            return translated;
        }

        /// <summary>
        /// Exécute une traduction complète en tâche de fond : parse, traduit, écrit le SRT.
        /// </summary>
        /// <param name="afterTranslation">
        /// Étape facultative exécutée avant que le job ne passe à Completed (ex: fusion bilingue).
        /// Elle reçoit les répliques source et traduites déjà en mémoire.
        /// </param>
        public static async Task RunJobAsync(
            TranslationJob job,
            string sourceSrtPath,
            string outputPath,
            string? sourceLanguage,
            string targetLanguage,
            string apiKey,
            string? model,
            int batchSize,
            Action<List<SubtitleCue>, List<SubtitleCue>>? afterTranslation = null)
        {
            try
            {
                job.Status = TranslationJobStatus.Running;
                job.Message = "Lecture du sous-titre source…";

                var cues = SrtParser.ParseFile(sourceSrtPath);
                job.Total = cues.Count;

                if (cues.Count == 0)
                    throw new InvalidOperationException("Le sous-titre source ne contient aucune réplique.");

                job.Message = $"Traduction de {cues.Count} répliques…";

                var translated = await TranslateCuesAsync(
                    cues, sourceLanguage, targetLanguage, apiKey, model,
                    batchSize, job, job.Cancellation.Token);

                SrtParser.WriteFile(outputPath, translated);

                job.OutputPath = outputPath;
                job.Message = job.Untranslated > 0
                    ? $"Traduction terminée : {translated.Count} répliques ({job.Untranslated} non traduites, texte d'origine conservé)."
                    : $"Traduction terminée : {translated.Count} répliques.";

                Plugin.Log($"[Translate] Terminé: {outputPath} ({job.InputTokens} tokens in / {job.OutputTokens} out)");

                // Avant Completed : la page arrête de suivre le job dès qu'elle voit ce statut
                afterTranslation?.Invoke(cues, translated);

                job.Status = TranslationJobStatus.Completed;
            }
            catch (OperationCanceledException)
            {
                job.Status = TranslationJobStatus.Cancelled;
                job.Message = "Traduction annulée.";
                Plugin.Log("[Translate] Annulé par l'utilisateur");
            }
            catch (Exception ex)
            {
                job.Status = TranslationJobStatus.Failed;
                job.Error = ex.Message;
                job.Message = "Échec de la traduction.";
                Plugin.Log($"[Translate] ERREUR: {ex}");
            }
            finally
            {
                job.FinishedAt = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Nom anglais d'une langue pour le prompt.
        /// Un code inconnu est renvoyé tel quel : le modèle sait interpréter un code comme "ca".
        /// </summary>
        public static string LanguageName(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "the source language of the cues";

            // Variantes régionales que le catalogue ramènerait à la langue de base
            switch (code!.Trim().ToLowerInvariant())
            {
                case "pt-br": return "Brazilian Portuguese";
                case "zh-hant": return "Chinese (Traditional)";
            }

            return SubtitleNaming.Resolve(code)?.EnglishName ?? code;
        }
    }
}
