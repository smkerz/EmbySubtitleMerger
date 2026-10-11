using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Lance la traduction complète d'un sous-titre.
    /// Retourne immédiatement un identifiant de job : la traduction d'un film
    /// prend plusieurs minutes et ne tient pas dans une requête HTTP.
    /// </summary>
    [Route("/EmbySubtitleMerger/Translate", "POST")]
    [Authenticated(Roles = "Admin")]
    public class TranslateSubtitleRequest : IReturn<TranslateSubtitleResponse>
    {
        /// <summary>Chemin du fichier vidéo</summary>
        public string? VideoPath { get; set; }

        /// <summary>Index absolu de la piste source si elle est embarquée</summary>
        public int SourceIndex { get; set; }

        /// <summary>Indique si la source est un fichier externe</summary>
        public bool SourceIsExternal { get; set; } = false;

        /// <summary>Chemin du fichier source si externe</summary>
        public string? SourcePath { get; set; }

        /// <summary>Code langue de la source (ex: en, eng). Vide = le modèle la reconnaît seul</summary>
        public string? SourceLanguage { get; set; }

        /// <summary>Code langue cible (ex: fr)</summary>
        public string? TargetLanguage { get; set; }

        /// <summary>Moteur : "google" (gratuit, sans clé) ou "claude" (payant, avec clé). À défaut, celui des réglages</summary>
        public string? Engine { get; set; }

        /// <summary>Clé API Anthropic pour le moteur Claude. À défaut, celle de la configuration du plugin est utilisée</summary>
        public string? AnthropicApiKey { get; set; }

        /// <summary>Modèle à utiliser. À défaut, celui de la configuration du plugin</summary>
        public string? Model { get; set; }

        /// <summary>Nombre de répliques par appel API (1 à 100)</summary>
        public int? BatchSize { get; set; }

        /// <summary>Remplacer le sous-titre "Film.xx.srt" s'il existe déjà. Sinon la traduction est refusée</summary>
        public bool Overwrite { get; set; } = false;

        /// <summary>Enchaîner une fusion traduction + source pour produire un sous-titre bilingue</summary>
        public bool ThenMerge { get; set; } = false;

        /// <summary>Mode de fusion si ThenMerge: all, overlapping, primary</summary>
        public string? MergeMode { get; set; }

        /// <summary>Tolérance de fusion en ms</summary>
        public int? ToleranceMs { get; set; }

        /// <summary>Couleur de la traduction (langue 1 du bilingue)</summary>
        public string? Color1 { get; set; }

        /// <summary>Couleur de la source (langue 2 du bilingue)</summary>
        public string? Color2 { get; set; }

        /// <summary>Disposition du bilingue: topbottom (defaut) ou stacked (tout en bas)</summary>
        public string? Layout { get; set; }
    }

    public class TranslateSubtitleResponse
    {
        public bool Success { get; set; }
        public string? JobId { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Suivi de progression d'un job de traduction
    /// </summary>
    [Route("/EmbySubtitleMerger/TranslateStatus", "GET")]
    [Authenticated(Roles = "Admin")]
    public class TranslateStatusRequest : IReturn<TranslateStatusResponse>
    {
        public string? JobId { get; set; }
    }

    public class TranslateStatusResponse
    {
        public bool Found { get; set; }
        public string? Status { get; set; }
        public int Processed { get; set; }
        public int Total { get; set; }
        public int PercentComplete { get; set; }
        public int Untranslated { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public string? OutputPath { get; set; }
        public string? MergedPath { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }

    /// <summary>
    /// Annulation d'un job en cours
    /// </summary>
    [Route("/EmbySubtitleMerger/TranslateCancel", "POST")]
    [Authenticated(Roles = "Admin")]
    public class TranslateCancelRequest : IReturn<TranslateCancelResponse>
    {
        public string? JobId { get; set; }
    }

    public class TranslateCancelResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Service API pour la traduction de sous-titres
    /// </summary>
    public class TranslateService : IService
    {
        public object Post(TranslateSubtitleRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.VideoPath) || !File.Exists(request.VideoPath))
                    return Fail("Fichier vidéo introuvable.");

                var config = Plugin.Instance?.Configuration;

                var targetCode = FirstNonBlank(request.TargetLanguage, config?.DefaultTargetLanguage);
                if (targetCode == null)
                    return Fail("Langue cible requise.");

                // Le nom du fichier produit vient du catalogue, jamais du texte de la requête
                var target = SubtitleNaming.Resolve(targetCode);
                if (target == null)
                    return Fail($"Langue cible inconnue: {targetCode}");

                var engine = (FirstNonBlank(request.Engine, config?.TranslationEngine) ?? TranslationEngines.Google).Trim().ToLowerInvariant();
                if (!TranslationEngines.IsKnown(engine))
                    return Fail($"Moteur de traduction inconnu: {request.Engine}");

                var apiKey = FirstNonBlank(request.AnthropicApiKey, config?.AnthropicApiKey);
                if (engine == TranslationEngines.Claude && apiKey == null)
                    return Fail("Clé API Anthropic requise.");

                var sourceLanguage = SubtitleNaming.Resolve(request.SourceLanguage);
                if (sourceLanguage != null && sourceLanguage.Code == target.Code)
                    return Fail($"La piste source est déjà en {target.NativeName}.");

                var outputPath = SubtitleNaming.SinglePath(request.VideoPath, target);
                if (request.SourceIsExternal && SubtitleNaming.SamePath(outputPath, request.SourcePath ?? string.Empty))
                    return Fail("La traduction écraserait le sous-titre source.");

                // Un Film.fr.srt déjà là peut être un vrai sous-titre : on ne le remplace que sur demande
                if (File.Exists(outputPath) && !request.Overwrite)
                    return Fail($"{Path.GetFileName(outputPath)} existe déjà. Cochez « Remplacer le sous-titre existant » pour l'écraser.");

                var batchSize = Math.Clamp(
                    request.BatchSize ?? config?.TranslationBatchSize ?? SubtitleTranslator.DefaultBatchSize, 1, 100);
                var model = FirstNonBlank(request.Model, config?.TranslationModel);

                // Le job ne garde que ce dont il a besoin, pas la requête entière
                var videoPath = request.VideoPath;
                var sourceIsExternal = request.SourceIsExternal;
                var sourcePath = request.SourcePath;
                var sourceIndex = request.SourceIndex;
                var sourceCode = request.SourceLanguage;
                var mergeOptions = request.ThenMerge
                    ? MergeOptions.From(request.MergeMode, request.ToleranceMs, request.Color1, request.Color2, request.Layout)
                    : null;

                var job = JobStore<TranslationJob>.Add(new TranslationJob());

                Plugin.Log($"[Translate] Job {job.Id}: {Path.GetFileName(videoPath)} -> {target.Code}, moteur {engine}, lots de {batchSize}");

                // Exécution en tâche de fond, suivi via /TranslateStatus. L'extraction de la piste en fait
                // partie : sur un gros film elle dure plusieurs minutes, trop pour la requête de lancement.
                _ = Task.Run(async () =>
                {
                    job.Status = JobStatus.Running;
                    job.Message = "Lecture du sous-titre source…";

                    SubtitleSource source;
                    try
                    {
                        source = await SubtitleSource.AcquireAsync(
                            videoPath, sourceIsExternal, sourcePath, sourceIndex, "sous-titre source", job.Cancellation.Token);
                    }
                    catch (Exception ex)
                    {
                        var cancelled = ex is OperationCanceledException;
                        job.Error = cancelled ? null : ex.Message;
                        job.Message = cancelled ? "Traduction annulée." : "Échec de la traduction.";
                        job.FinishedAt = DateTime.UtcNow;
                        job.Status = cancelled ? JobStatus.Cancelled : JobStatus.Failed;
                        return;
                    }

                    if (!source.Success)
                    {
                        job.Error = source.Error;
                        job.Message = "Échec de la traduction.";
                        job.FinishedAt = DateTime.UtcNow;
                        job.Status = JobStatus.Failed;
                        return;
                    }

                    try
                    {
                        await SubtitleTranslator.RunJobAsync(
                            job, source.Path, outputPath, sourceCode, targetCode, engine, apiKey, model, batchSize,
                            mergeOptions == null
                                ? null
                                : (sourceCues, translatedCues) => WriteBilingual(
                                    job, translatedCues, sourceCues, videoPath, target, sourceLanguage, mergeOptions));
                    }
                    finally
                    {
                        source.Cleanup();
                    }
                });

                return new TranslateSubtitleResponse
                {
                    Success = true,
                    JobId = job.Id,
                    Message = "Traduction lancée."
                };
            }
            catch (Exception ex)
            {
                Plugin.Log($"[Translate] ERREUR au démarrage: {ex}");
                return Fail(ex.Message);
            }
        }

        private static TranslateSubtitleResponse Fail(string error)
        {
            return new TranslateSubtitleResponse { Success = false, Error = error };
        }

        private static string? FirstNonBlank(string? first, string? second)
        {
            if (!string.IsNullOrWhiteSpace(first)) return first;
            return string.IsNullOrWhiteSpace(second) ? null : second;
        }

        /// <summary>
        /// Fusionne la traduction avec la source pour produire un sous-titre bilingue.
        /// La traduction est la langue 1 (en haut en disposition haut/bas), la source la langue 2.
        /// </summary>
        private static void WriteBilingual(
            TranslationJob job,
            List<SubtitleCue> translatedCues,
            List<SubtitleCue> sourceCues,
            string videoPath,
            LanguageInfo target,
            LanguageInfo? sourceLanguage,
            MergeOptions options)
        {
            try
            {
                job.Message = "Fusion du sous-titre bilingue…";

                var bottom = sourceLanguage ?? SubtitleNaming.Resolve(LanguageDetector.DetectSingle(sourceCues));
                var mergedPath = SubtitleNaming.DualPath(videoPath, target, bottom);

                var merged = SubtitleMerger.Merge(translatedCues, sourceCues, options);
                SrtParser.WriteFile(mergedPath, merged);

                job.MergedPath = mergedPath;
                job.Message = $"Traduction et fusion terminées : {merged.Count} répliques.";

                Plugin.Log($"[Translate] Fusion post-traduction: {mergedPath}");
            }
            catch (Exception ex)
            {
                // La traduction est valide même si la fusion échoue : on ne bascule pas le job en échec
                job.Message = $"Traduction terminée, mais la fusion a échoué : {ex.Message}";
                Plugin.Log($"[Translate] ERREUR fusion post-traduction: {ex}");
            }
        }

        public object Get(TranslateStatusRequest request)
        {
            var job = JobStore<TranslationJob>.Get(request.JobId);

            if (job == null)
            {
                return new TranslateStatusResponse
                {
                    Found = false,
                    Error = "Job introuvable ou expiré."
                };
            }

            return new TranslateStatusResponse
            {
                Found = true,
                Status = job.Status.ToString(),
                Processed = job.Processed,
                Total = job.Total,
                PercentComplete = job.PercentComplete,
                Untranslated = job.Untranslated,
                Message = job.Message,
                Error = job.Error,
                OutputPath = job.OutputPath,
                MergedPath = job.MergedPath,
                InputTokens = job.InputTokens,
                OutputTokens = job.OutputTokens
            };
        }

        public object Post(TranslateCancelRequest request)
        {
            var cancelled = JobStore<TranslationJob>.Cancel(request.JobId);
            return new TranslateCancelResponse
            {
                Success = cancelled,
                Message = cancelled ? "Annulation demandée." : "Aucun job actif avec cet identifiant."
            };
        }
    }
}
