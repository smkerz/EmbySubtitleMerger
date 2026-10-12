using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Job de traduction d'un sous-titre
    /// </summary>
    public class TranslationJob : BackgroundJob
    {
        /// <summary>Répliques que le modèle n'a pas renvoyées et qui gardent le texte d'origine</summary>
        public int Untranslated { get; set; }

        /// <summary>Chemin du SRT traduit</summary>
        public string? OutputPath { get; set; }

        /// <summary>Chemin du SRT bilingue, si une fusion a suivi la traduction</summary>
        public string? MergedPath { get; set; }

        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }

    /// <summary>
    /// Un service de traduction : il reçoit un lot de répliques numérotées et rend leurs traductions
    /// sous les mêmes numéros. Les langues sont fixées à la création du moteur.
    /// </summary>
    public interface ITranslationEngine : IDisposable
    {
        /// <param name="context">Répliques précédentes déjà traduites, pour la continuité ; les moteurs sans mémoire l'ignorent</param>
        Task<TranslationBatchResult> TranslateBatchAsync(
            IReadOnlyList<TranslationItem> batch,
            IReadOnlyList<TranslationItem> context,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Le moteur de traduction a refusé ou n'a pas pu traiter un lot (quota atteint, clé refusée, service en panne)
    /// </summary>
    public class TranslationEngineException : InvalidOperationException
    {
        public TranslationEngineException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Moteurs de traduction proposés
    /// </summary>
    public static class TranslationEngines
    {
        /// <summary>API Claude : payante, tient compte du contexte, nécessite une clé</summary>
        public const string Claude = "claude";

        /// <summary>Service web de Google Traduction : gratuit, sans clé, non officiel</summary>
        public const string Google = "google";

        public static bool IsKnown(string? name) => name == Claude || name == Google;

        /// <summary>
        /// Répliques confiées au moteur à chaque appel. Google découpe lui-même par taille de requête :
        /// un grand lot lui évite des requêtes presque vides. Claude traduit mieux par lots modérés.
        /// </summary>
        public static int BatchSize(string engine, int? requested = null)
        {
            return engine == Google
                ? GoogleTranslateClient.CuesPerCall
                : Math.Clamp(requested ?? SubtitleTranslator.DefaultBatchSize, 1, 100);
        }

        public static ITranslationEngine Create(string name, string? apiKey, string? model, string? sourceLanguage, string targetLanguage)
        {
            if (name == Google)
                return new GoogleTranslateClient(sourceLanguage, targetLanguage);

            return new ClaudeEngine(apiKey ?? string.Empty, model, sourceLanguage, targetLanguage);
        }

        private sealed class ClaudeEngine : ITranslationEngine
        {
            private readonly ClaudeTranslationClient _client;
            private readonly string _sourceName;
            private readonly string _targetName;

            public ClaudeEngine(string apiKey, string? model, string? sourceLanguage, string targetLanguage)
            {
                _client = new ClaudeTranslationClient(apiKey, model);
                _sourceName = SubtitleTranslator.LanguageName(sourceLanguage);
                _targetName = SubtitleTranslator.LanguageName(targetLanguage);
            }

            public Task<TranslationBatchResult> TranslateBatchAsync(
                IReadOnlyList<TranslationItem> batch, IReadOnlyList<TranslationItem> context, CancellationToken cancellationToken = default)
            {
                return _client.TranslateBatchAsync(batch, context, _sourceName, _targetName, cancellationToken);
            }

            public void Dispose() => _client.Dispose();
        }
    }

    /// <summary>
    /// Répliques déjà traduites d'une traduction en cours, gardées sur disque après chaque lot.
    /// Une traduction interrompue (erreur, annulation, redémarrage) reprend là où elle s'était arrêtée
    /// au lieu de tout refaire. Le fichier est lié au texte source, à la langue cible et au moteur.
    /// </summary>
    public sealed class TranslationCheckpoint
    {
        private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

        private readonly string _path;

        /// <summary>Traductions acquises, par position de la réplique</summary>
        public Dictionary<int, string> Done { get; private set; } = new Dictionary<int, string>();

        private TranslationCheckpoint(string path)
        {
            _path = path;
        }

        public static TranslationCheckpoint Open(IReadOnlyList<SubtitleCue> cues, string targetLanguage, string engineName)
        {
            var folder = Path.Combine(Path.GetTempPath(), "EmbySubtitleMerger");
            Directory.CreateDirectory(folder);
            PurgeOld(folder);

            var signature = engineName + "|" + targetLanguage + "|" + string.Join("\n", cues.Select(c => c.Text));
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(signature)));

            var checkpoint = new TranslationCheckpoint(Path.Combine(folder, $"traduction-{hash}.json"));
            try
            {
                if (File.Exists(checkpoint._path))
                {
                    var saved = JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(checkpoint._path));
                    if (saved != null)
                        checkpoint.Done = saved.Where(e => e.Key >= 0 && e.Key < cues.Count).ToDictionary(e => e.Key, e => e.Value);
                }
            }
            catch
            {
                // Fichier abîmé : on repart de zéro
            }

            return checkpoint;
        }

        public void Save()
        {
            try { File.WriteAllText(_path, JsonSerializer.Serialize(Done)); } catch { }
        }

        public void Delete()
        {
            try { File.Delete(_path); } catch { }
        }

        private static void PurgeOld(string folder)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "traduction-*.json"))
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > Retention)
                        File.Delete(file);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Traduit un fichier SRT complet avec le moteur choisi.
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
            ITranslationEngine engine,
            int batchSize = DefaultBatchSize,
            TranslationJob? job = null,
            CancellationToken cancellationToken = default,
            TranslationCheckpoint? checkpoint = null)
        {
            if (cues == null) throw new ArgumentNullException(nameof(cues));

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

            // Répliques acquises lors d'une tentative précédente
            var alreadyDone = checkpoint?.Done ?? new Dictionary<int, string>();
            foreach (var entry in alreadyDone)
                translated[entry.Key].Text = entry.Value;

            // Id = position dans la liste, stable et indépendant de l'index SRT d'origine
            var items = translated
                .Select((c, i) => new TranslationItem { Id = i, Text = c.Text })
                .Where(item => !alreadyDone.ContainsKey(item.Id))
                .ToList();

            var doneBefore = translated.Count - items.Count;
            if (job != null) job.Processed = doneBefore;

            var previousBatchTranslations = new List<TranslationItem>();

            for (int offset = 0; offset < items.Count; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = items.Skip(offset).Take(batchSize).ToList();
                var context = previousBatchTranslations
                    .Skip(Math.Max(0, previousBatchTranslations.Count - ContextSize))
                    .ToList();

                var result = await engine.TranslateBatchAsync(batch, context, cancellationToken);

                if (!result.Success)
                    throw new TranslationEngineException(result.Error ?? "Échec de la traduction du lot.");

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

                    var retry = await engine.TranslateBatchAsync(missing, context, cancellationToken);

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
                        if (checkpoint != null) checkpoint.Done[item.Id] = text;
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
                checkpoint?.Save();

                if (job != null)
                {
                    job.Processed = doneBefore + offset + batch.Count;
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
            string engineName,
            string? apiKey,
            string? model,
            int batchSize,
            Action<List<SubtitleCue>, List<SubtitleCue>>? afterTranslation = null)
        {
            try
            {
                job.Status = JobStatus.Running;
                job.Message = "Lecture du sous-titre source…";

                var cues = SrtParser.ParseFile(sourceSrtPath);
                job.Total = cues.Count;

                if (cues.Count == 0)
                    throw new InvalidOperationException("Le sous-titre source ne contient aucune réplique.");

                var checkpoint = TranslationCheckpoint.Open(cues, targetLanguage, engineName);
                job.Message = checkpoint.Done.Count > 0
                    ? $"Reprise : {checkpoint.Done.Count} répliques sur {cues.Count} déjà traduites…"
                    : $"Traduction de {cues.Count} répliques…";

                using var engine = TranslationEngines.Create(engineName, apiKey, model, sourceLanguage, targetLanguage);

                var translated = await TranslateCuesAsync(cues, engine, batchSize, job, job.Cancellation.Token, checkpoint);

                SrtParser.WriteFile(outputPath, translated);
                checkpoint.Delete();

                job.OutputPath = outputPath;
                job.Message = job.Untranslated > 0
                    ? $"Traduction terminée : {translated.Count} répliques ({job.Untranslated} non traduites, texte d'origine conservé)."
                    : $"Traduction terminée : {translated.Count} répliques.";

                Plugin.Log($"[Translate] Terminé: {outputPath} ({job.InputTokens} tokens in / {job.OutputTokens} out)");

                // Avant Completed : la page arrête de suivre le job dès qu'elle voit ce statut
                afterTranslation?.Invoke(cues, translated);

                job.Status = JobStatus.Completed;
            }
            catch (OperationCanceledException)
            {
                job.Status = JobStatus.Cancelled;
                job.Message = "Traduction annulée.";
                Plugin.Log("[Translate] Annulé par l'utilisateur");
            }
            catch (Exception ex)
            {
                job.Status = JobStatus.Failed;
                job.Error = ex.Message;
                job.Message = "Échec de la traduction.";
                if (job.Processed > 0)
                    job.Error += $" Les {job.Processed} répliques déjà traduites sont gardées : relancez la traduction pour reprendre.";
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
