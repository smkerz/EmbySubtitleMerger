using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Dossiers des médiathèques d'Emby
    /// </summary>
    internal static class LibraryFolders
    {
        public static List<string> All(ILibraryManager libraryManager)
        {
            return Of(libraryManager, null, null)!;
        }

        /// <summary>
        /// Dossiers d'une seule médiathèque, désignée par son identifiant Emby ou à défaut par son nom.
        /// Sans identifiant ni nom : toutes les médiathèques. Null si la médiathèque demandée n'existe pas.
        /// </summary>
        public static List<string>? Of(ILibraryManager libraryManager, string? libraryId, string? libraryName)
        {
            var libraries = libraryManager.GetVirtualFolders().AsEnumerable();

            if (!string.IsNullOrWhiteSpace(libraryId) || !string.IsNullOrWhiteSpace(libraryName))
            {
                var byId = libraries.Where(f => !string.IsNullOrWhiteSpace(libraryId) && string.Equals(f.ItemId, libraryId, StringComparison.OrdinalIgnoreCase)).ToList();
                libraries = byId.Count > 0
                    ? byId
                    : libraries.Where(f => !string.IsNullOrWhiteSpace(libraryName) && string.Equals(f.Name, libraryName, StringComparison.OrdinalIgnoreCase)).ToList();

                if (!libraries.Any()) return null;
            }

            return libraries
                .SelectMany(f => f.Locations ?? Array.Empty<string>())
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// Programmes externes disponibles sur le serveur
    /// </summary>
    [Route("/EmbySubtitleMerger/Tools", "GET")]
    [Authenticated(Roles = "Admin")]
    public class GetToolsRequest : IReturn<ToolsResponse>
    {
    }

    public class ToolsResponse
    {
        public bool FfmpegAvailable { get; set; }
        public bool FfprobeAvailable { get; set; }

        /// <summary>mkvpropedit (MKVToolNix) : nécessaire seulement pour annoter les pistes des .mkv</summary>
        public bool MkvPropEditAvailable { get; set; }
    }

    /// <summary>
    /// Lance la normalisation des sous-titres (conversion des ASS/SSA en SRT), en tâche de fond.
    /// </summary>
    [Route("/EmbySubtitleMerger/Normalize", "POST")]
    [Authenticated(Roles = "Admin")]
    public class NormalizeRequest : IReturn<NormalizeResponse>
    {
        /// <summary>true (défaut) = aperçu, rien n'est modifié</summary>
        public bool DryRun { get; set; } = true;

        /// <summary>Limite le traitement à cette vidéo. Vide = toutes les médiathèques, ou celle désignée ci-dessous</summary>
        public string? VideoPath { get; set; }

        /// <summary>Sans VideoPath : limite le traitement à cette médiathèque (identifiant Emby)</summary>
        public string? LibraryId { get; set; }

        /// <summary>Nom de la médiathèque, utilisé si l'identifiant n'est pas reconnu</summary>
        public string? LibraryName { get; set; }

        /// <summary>Langues à convertir, séparées par des virgules (ex: "en, fr, ru")</summary>
        public string? Languages { get; set; }

        /// <summary>Refaire les .srt qui existent déjà</summary>
        public bool Overwrite { get; set; }

        /// <summary>Nettoyer au lieu de normaliser : retire les .srt créés par la normalisation</summary>
        public bool Cleanup { get; set; }

        /// <summary>En nettoyage, retirer aussi les conversions de pistes ASS/SSA (pas seulement les copies de pistes texte)</summary>
        public bool CleanupConversions { get; set; }

        /// <summary>En nettoyage, retirer aussi les sous-titres bilingues créés par la fusion</summary>
        public bool CleanupBilingual { get; set; }

        /// <summary>En nettoyage, retirer aussi la note "Normalisé pour Emby" des titres de pistes des .mkv et des .srt d'origine renommés</summary>
        public bool CleanupNote { get; set; }

        /// <summary>
        /// Fusion en série au lieu de normaliser : crée le bilingue Language1 + Language2 des vidéos
        /// qui ont les deux langues. Avec VideoPath, porte sur tout le dossier de cette vidéo.
        /// </summary>
        public bool BatchMerge { get; set; }

        /// <summary>Fusion en série : langue du haut (ex: en)</summary>
        public string? Language1 { get; set; }

        /// <summary>Fusion en série : langue du bas (ex: ru)</summary>
        public string? Language2 { get; set; }

        /// <summary>Fusion en série : all, overlapping ou primary</summary>
        public string? MergeMode { get; set; }

        public int? ToleranceMs { get; set; }

        /// <summary>Fusion en série : topbottom (défaut) ou stacked</summary>
        public string? Layout { get; set; }

        /// <summary>Fusion en série : marquer les bilingues "default"</summary>
        public bool MarkDefault { get; set; }

        /// <summary>
        /// Fusion en série : traduire la ou les langues absentes, avec le moteur des réglages de traduction
        /// </summary>
        public bool BatchTranslate { get; set; }
    }

    public class NormalizeResponse
    {
        public bool Success { get; set; }
        public string? JobId { get; set; }
        public string? Error { get; set; }

        /// <summary>
        /// true si une autre tâche tournait déjà : JobId est le sien, et la demande n'a pas été lancée
        /// </summary>
        public bool AlreadyRunning { get; set; }
    }

    [Route("/EmbySubtitleMerger/NormalizeStatus", "GET")]
    [Authenticated(Roles = "Admin")]
    public class NormalizeStatusRequest : IReturn<NormalizeStatusResponse>
    {
        public string? JobId { get; set; }
    }

    public class NormalizeStatusResponse
    {
        public bool Found { get; set; }
        public string? Status { get; set; }
        public bool DryRun { get; set; }
        public bool Cleanup { get; set; }
        public bool BatchMerge { get; set; }
        public int Merged { get; set; }
        public int Unmatched { get; set; }
        public int Translated { get; set; }
        public int WithoutSubtitle { get; set; }
        public int Processed { get; set; }
        public int Total { get; set; }
        public int PercentComplete { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }

        public int Created { get; set; }
        public int Renamed { get; set; }
        public int Tagged { get; set; }
        public int Deleted { get; set; }
        public int Failures { get; set; }

        /// <summary>Détail des opérations, fourni une fois la tâche terminée</summary>
        public List<NormalizeAction> Actions { get; set; } = new List<NormalizeAction>();

        /// <summary>true si la liste a été coupée (les compteurs restent exacts)</summary>
        public bool ActionsTruncated { get; set; }
    }

    [Route("/EmbySubtitleMerger/NormalizeCancel", "POST")]
    [Authenticated(Roles = "Admin")]
    public class NormalizeCancelRequest : IReturn<NormalizeCancelResponse>
    {
        public string? JobId { get; set; }
    }

    public class NormalizeCancelResponse
    {
        public bool Success { get; set; }
    }

    public class NormalizeService : IService
    {
        /// <summary>Langues converties quand la requête n'en précise pas, comme dans le script d'origine</summary>
        private const string DefaultLanguages = PluginConfiguration.DefaultNormalizeLanguages;

        private readonly ILibraryManager _libraryManager;

        public NormalizeService(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        public async Task<object> Get(GetToolsRequest request)
        {
            var ffmpeg = ExternalTools.RespondsAsync(FfmpegHelper.FfmpegPath);
            var ffprobe = ExternalTools.RespondsAsync(FfmpegHelper.FfprobePath);

            return new ToolsResponse
            {
                FfmpegAvailable = await ffmpeg,
                FfprobeAvailable = await ffprobe,
                MkvPropEditAvailable = ExternalTools.FindMkvPropEdit() != null
            };
        }

        public async Task<object> Post(NormalizeRequest request)
        {
            try
            {
                // Deux normalisations en même temps écriraient les mêmes fichiers
                var running = JobStore<NormalizeJob>.Active();
                if (running != null)
                    return new NormalizeResponse { Success = true, JobId = running.Id, AlreadyRunning = true };

                if (request.Cleanup && request.BatchMerge)
                    return Fail("Nettoyage et fusion en série ne se demandent pas ensemble.");

                // Le nettoyage ne convertit rien : il lit seulement les vidéos avec ffprobe
                if (!await ExternalTools.RespondsAsync(FfmpegHelper.FfprobePath)
                    || (!request.Cleanup && !await ExternalTools.RespondsAsync(FfmpegHelper.FfmpegPath)))
                {
                    return Fail("ffmpeg ou ffprobe est introuvable sur le serveur.");
                }

                var options = new NormalizeOptions
                {
                    DryRun = request.DryRun,
                    Overwrite = request.Overwrite,
                    Cleanup = request.Cleanup,
                    CleanupConversions = request.CleanupConversions,
                    CleanupBilingual = request.CleanupBilingual,
                    CleanupNote = request.Cleanup && request.CleanupNote,
                    BatchMerge = request.BatchMerge,
                    MarkDefault = request.MarkDefault,
                    WholeFolder = request.BatchMerge
                };

                if (options.BatchMerge)
                {
                    options.BatchLanguage1 = SubtitleNaming.Resolve(request.Language1);
                    options.BatchLanguage2 = SubtitleNaming.Resolve(request.Language2);

                    if (options.BatchLanguage1 == null || options.BatchLanguage2 == null)
                        return Fail("Choisissez les deux langues à fusionner.");
                    if (options.BatchLanguage1.Code == options.BatchLanguage2.Code)
                        return Fail("Choisissez deux langues différentes.");

                    options.BatchMergeOptions = MergeOptions.From(request.MergeMode, request.ToleranceMs, null, null, request.Layout);

                    if (request.BatchTranslate)
                    {
                        // Même moteur et même clé que l'onglet Traduire
                        var config = Plugin.Instance?.Configuration;
                        var engine = TranslationEngines.IsKnown(config?.TranslationEngine) ? config!.TranslationEngine : TranslationEngines.Google;
                        var apiKey = config?.AnthropicApiKey;
                        var model = config?.TranslationModel;

                        if (engine == TranslationEngines.Claude && string.IsNullOrWhiteSpace(apiKey))
                            return Fail("Le moteur de traduction choisi est Claude, mais aucune clé API n'est enregistrée (onglet Traduire).");

                        options.BatchTranslate = true;
                        options.TranslationEngineName = engine;
                        options.CreateTranslationEngine = (source, target) => TranslationEngines.Create(engine, apiKey, model, source, target);
                    }
                }

                // Les langues ne servent qu'à la normalisation
                var languageList = string.IsNullOrWhiteSpace(request.Languages) ? DefaultLanguages : request.Languages!;
                foreach (var code in options.VideosOnly
                    ? Array.Empty<string>()
                    : languageList.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var language = SubtitleNaming.Resolve(code);
                    if (language == null)
                        return Fail($"Langue inconnue: {code}");
                    options.Languages.Add(language.Code);
                }

                if (!string.IsNullOrWhiteSpace(request.VideoPath))
                {
                    if (!File.Exists(request.VideoPath))
                        return Fail("Fichier vidéo introuvable.");
                    options.VideoPath = request.VideoPath;
                }

                // Sans mkvpropedit, le retrait de la note se limite aux noms de fichiers
                if (options.CleanupNote)
                    options.MkvPropEditPath = ExternalTools.FindMkvPropEdit();

                // La tâche planifiée reprend les langues de la dernière normalisation complète
                var plugin = Plugin.Instance;
                var chosen = string.Join(",", options.Languages);
                if (plugin != null && !options.VideosOnly && !options.DryRun && options.VideoPath == null
                    && plugin.Configuration.NormalizeLanguages != chosen)
                {
                    plugin.Configuration.NormalizeLanguages = chosen;
                    plugin.SaveConfiguration();
                }

                var folders = options.VideoPath == null
                    ? LibraryFolders.Of(_libraryManager, request.LibraryId, request.LibraryName)
                    : new List<string>();
                if (folders == null)
                    return Fail("Médiathèque introuvable sur le serveur.");
                var job = JobStore<NormalizeJob>.Add(new NormalizeJob { DryRun = options.DryRun, Cleanup = options.Cleanup, BatchMerge = options.BatchMerge });

                Plugin.Log($"[Normalize] Job {job.Id}: {(options.BatchMerge ? "fusion en série" : options.Cleanup ? "nettoyage" : "normalisation")} ({(options.DryRun ? "aperçu" : "exécution")}), "
                    + $"{(options.VideoPath == null ? folders.Count + " dossier(s)" : Path.GetFileName(options.VideoPath))}, "
                    + $"langues {string.Join(",", options.Languages)}");

                var libraryManager = _libraryManager;
                _ = Task.Run(() => SubtitleNormalizer.RunAsync(job, folders, options, () => libraryManager.QueueLibraryScan()));

                return new NormalizeResponse { Success = true, JobId = job.Id };
            }
            catch (Exception ex)
            {
                Plugin.Log($"[Normalize] ERREUR au démarrage: {ex}");
                return Fail(ex.Message);
            }
        }

        private static NormalizeResponse Fail(string error)
        {
            return new NormalizeResponse { Success = false, Error = error };
        }

        public object Get(NormalizeStatusRequest request)
        {
            var job = JobStore<NormalizeJob>.Get(request.JobId);
            if (job == null)
                return new NormalizeStatusResponse { Found = false, Error = "Tâche introuvable ou expirée." };

            return new NormalizeStatusResponse
            {
                Found = true,
                Status = job.Status.ToString(),
                DryRun = job.DryRun,
                Cleanup = job.Cleanup,
                BatchMerge = job.BatchMerge,
                Merged = job.Merged,
                Unmatched = job.Unmatched,
                Translated = job.Translated,
                WithoutSubtitle = job.WithoutSubtitle,
                Processed = job.Processed,
                Total = job.Total,
                PercentComplete = job.PercentComplete,
                Message = job.Message,
                Error = job.Error,
                Created = job.Created,
                Renamed = job.Renamed,
                Tagged = job.Tagged,
                Deleted = job.Deleted,
                Failures = job.Failures,
                Actions = job.IsActive ? new List<NormalizeAction>() : job.SnapshotActions(),
                ActionsTruncated = job.ActionsTruncated
            };
        }

        public object Post(NormalizeCancelRequest request)
        {
            return new NormalizeCancelResponse { Success = JobStore<NormalizeJob>.Cancel(request.JobId) };
        }
    }
}
