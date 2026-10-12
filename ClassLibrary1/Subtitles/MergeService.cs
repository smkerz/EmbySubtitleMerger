using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Requête pour fusionner des sous-titres
    /// </summary>
    [Route("/EmbySubtitleMerger/Merge", "POST")]
    [Authenticated(Roles = "Admin")]
    public class MergeSubtitlesRequest : IReturn<MergeSubtitlesResponse>
    {
        /// <summary>Chemin du fichier vidéo</summary>
        public string? VideoPath { get; set; }

        /// <summary>Index du premier sous-titre (haut)</summary>
        public int PrimaryIndex { get; set; }

        /// <summary>Index du second sous-titre (bas)</summary>
        public int SecondaryIndex { get; set; }

        /// <summary>Indique si le sous-titre 1 est externe (fichier séparé)</summary>
        public bool Primary1IsExternal { get; set; } = false;

        /// <summary>Chemin du fichier sous-titre 1 si externe</summary>
        public string? Primary1Path { get; set; }

        /// <summary>Indique si le sous-titre 2 est externe (fichier séparé)</summary>
        public bool Primary2IsExternal { get; set; } = false;

        /// <summary>Chemin du fichier sous-titre 2 si externe</summary>
        public string? Primary2Path { get; set; }

        /// <summary>Mode de fusion: AllCues, OverlappingOnly, PrimaryPriority</summary>
        public string? Mode { get; set; }

        /// <summary>Tolérance en ms</summary>
        public int? ToleranceMs { get; set; }


        /// <summary>Decalage en ms pour le sous-titre 1</summary>
        public int Offset1Ms { get; set; } = 0;

        /// <summary>Decalage en ms pour le sous-titre 2</summary>
        public int Offset2Ms { get; set; } = 0;


        /// <summary>Code langue du sous-titre 1 (ex: fre). Vide = détection sur le contenu</summary>
        public string? Language1 { get; set; }

        /// <summary>Code langue du sous-titre 2 (ex: eng). Vide = détection sur le contenu</summary>
        public string? Language2 { get; set; }

        /// <summary>Disposition: topbottom (defaut) ou stacked (tout en bas)</summary>
        public string? Layout { get; set; }

        /// <summary>
        /// true : la fusion tourne en tâche de fond et la réponse ne porte que JobId,
        /// à suivre avec /EmbySubtitleMerger/MergeStatus. L'extraction d'une piste d'un gros film
        /// dure plusieurs minutes : une requête aussi longue est coupée par un proxy ou le navigateur.
        /// </summary>
        public bool Background { get; set; } = false;

        /// <summary>
        /// Marquer le bilingue "default" dans son nom : Emby le sélectionne alors de lui-même,
        /// selon les réglages de lecture de l'utilisateur.
        /// </summary>
        public bool MarkDefault { get; set; } = false;
    }

    /// <summary>
    /// Requête de suivi d'une fusion lancée en tâche de fond
    /// </summary>
    [Route("/EmbySubtitleMerger/MergeStatus", "GET")]
    [Authenticated(Roles = "Admin")]
    public class GetMergeStatusRequest : IReturn<MergeSubtitlesResponse>
    {
        public string? JobId { get; set; }
    }

    /// <summary>
    /// Arrête une fusion en cours (l'extraction des pistes d'un gros film dure plusieurs minutes)
    /// </summary>
    [Route("/EmbySubtitleMerger/MergeCancel", "POST")]
    [Authenticated(Roles = "Admin")]
    public class CancelMergeRequest : IReturn<MergeSubtitlesResponse>
    {
        public string? JobId { get; set; }
    }

    /// <summary>
    /// Fusion en tâche de fond. Message porte l'étape en cours.
    /// </summary>
    public class MergeJob : BackgroundJob
    {
        public MergeSubtitlesResponse? Result { get; set; }
    }
    
    /// <summary>
    /// Réponse de la fusion
    /// </summary>
    public class MergeSubtitlesResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? OutputPath { get; set; }
        public int? CueCount { get; set; }
        public string? Error { get; set; }

        /// <summary>Tâche de fond : identifiant à passer à MergeStatus</summary>
        public string? JobId { get; set; }

        /// <summary>Tâche de fond : true tant que la fusion n'est pas terminée</summary>
        public bool Running { get; set; }

        /// <summary>Tâche de fond : étape en cours</summary>
        public string? Step { get; set; }
    }
    
    /// <summary>
    /// Service API pour la fusion de sous-titres
    /// </summary>
    public class MergeService : IService
    {
        /// <summary>
        /// Fusionne deux sous-titres
        /// </summary>
        public async Task<object> Post(MergeSubtitlesRequest request)
        {
            if (!request.Background)
                return await RunMergeAsync(request, null, default);

            var job = JobStore<MergeJob>.Add(new MergeJob { Status = JobStatus.Running, Message = "Démarrage" });

            _ = Task.Run(async () =>
            {
                var result = await RunMergeAsync(request, step => job.Message = step, job.Cancellation.Token);
                job.Result = result;
                job.FinishedAt = DateTime.UtcNow;
                job.Status = result.Success ? JobStatus.Completed : JobStatus.Failed;
            });

            return new MergeSubtitlesResponse { Success = true, Running = true, JobId = job.Id, Step = job.Message };
        }

        /// <summary>
        /// Avancement ou résultat d'une fusion lancée en tâche de fond
        /// </summary>
        public object Get(GetMergeStatusRequest request)
        {
            var job = JobStore<MergeJob>.Get(request.JobId);
            if (job == null)
                return Fail("Fusion introuvable : le serveur a peut-être redémarré. Relancez la fusion.");

            if (job.Result == null)
                return new MergeSubtitlesResponse { Success = true, Running = true, JobId = job.Id, Step = job.Message };

            return job.Result;
        }

        public object Post(CancelMergeRequest request)
        {
            return JobStore<MergeJob>.Cancel(request.JobId)
                ? new MergeSubtitlesResponse { Success = true }
                : Fail("Aucune fusion en cours à annuler.");
        }

        private static async Task<MergeSubtitlesResponse> RunMergeAsync(
            MergeSubtitlesRequest request, Action<string>? progress, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrEmpty(request.VideoPath))
                    return Fail("VideoPath est requis");

                if (!File.Exists(request.VideoPath))
                    return Fail($"Fichier vidéo non trouvé: {request.VideoPath}");

                progress?.Invoke("Étape 1/2 : lecture des deux sous-titres");
                var (source1, source2) = await SubtitleSource.AcquirePairAsync(
                    request.VideoPath,
                    request.Primary1IsExternal, request.Primary1Path, request.PrimaryIndex,
                    request.Primary2IsExternal, request.Primary2Path, request.SecondaryIndex,
                    cancellationToken);
                if (!source1.Success)
                    return Fail(source1.Error!);
                if (!source2.Success)
                    return Fail(source2.Error!);

                try
                {
                    progress?.Invoke("Étape 2/2 : fusion");

                    // Le nom du fichier porte les deux langues : c'est ce qu'Emby affiche dans le lecteur
                    var language1 = SubtitleNaming.ResolveOrDetect(request.Language1, source1.Path);
                    var language2 = SubtitleNaming.ResolveOrDetect(request.Language2, source2.Path);
                    var outputPath = SubtitleNaming.DualPath(request.VideoPath, language1, language2, request.MarkDefault);

                    if (SubtitleNaming.SamePath(outputPath, source1.Path) || SubtitleNaming.SamePath(outputPath, source2.Path))
                        return Fail("Le fichier produit écraserait l'un des deux sous-titres choisis. Choisissez deux autres pistes.");

                    return RemoveReplacedDuals(
                        MergeAndWrite(source1.Path, source2.Path, outputPath, request), request, language1, language2, source1, source2);
                }
                finally
                {
                    // Seuls les fichiers extraits par le plugin sont supprimés, jamais les externes
                    source1.Cleanup();
                    source2.Cleanup();
                }
            }
            catch (OperationCanceledException)
            {
                return Fail("Fusion annulée.");
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Après une fusion réussie, retire le même bilingue sous l'autre forme (avec ou sans "default") :
        /// sans cela, Emby le listerait en double.
        /// </summary>
        private static MergeSubtitlesResponse RemoveReplacedDuals(
            MergeSubtitlesResponse result,
            MergeSubtitlesRequest request,
            LanguageInfo? language1,
            LanguageInfo? language2,
            SubtitleSource source1,
            SubtitleSource source2)
        {
            if (!result.Success || result.OutputPath == null) return result;

            // Seulement le même bilingue sous l'autre forme. Un Film.dual.srt est laissé : rien ne dit
            // qu'il porte les mêmes langues (le nettoyage des bilingues sait le retirer sur demande).
            var replaced = new[]
            {
                SubtitleNaming.DualPath(request.VideoPath!, language1, language2, !request.MarkDefault)
            };

            foreach (var path in replaced)
            {
                if (SubtitleNaming.SamePath(path, result.OutputPath) || !File.Exists(path)
                    || SubtitleNaming.SamePath(path, source1.Path) || SubtitleNaming.SamePath(path, source2.Path))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                    result.Message += $" Ancien bilingue remplacé : {Path.GetFileName(path)}.";
                }
                catch (Exception ex)
                {
                    Plugin.Log($"[Merge] Ancien bilingue non retiré ({path}): {ex.Message}");
                }
            }

            return result;
        }

        private static MergeSubtitlesResponse Fail(string error)
        {
            return new MergeSubtitlesResponse { Success = false, Error = error };
        }

        /// <summary>
        /// Fusionne les deux SRT et écrit le bilingue
        /// </summary>
        private static MergeSubtitlesResponse MergeAndWrite(
            string srt1Path,
            string srt2Path,
            string outputPath,
            MergeSubtitlesRequest request)
        {
            try
            {
                var primary = SrtParser.ParseFile(srt1Path);
                var secondary = SrtParser.ParseFile(srt2Path);

                ApplyOffset(primary, request.Offset1Ms);
                ApplyOffset(secondary, request.Offset2Ms);

                var options = MergeOptions.From(request.Mode, request.ToleranceMs, request.Layout);
                var merged = SubtitleMerger.Merge(primary, secondary, options);

                SrtParser.WriteFile(outputPath, merged);

                return new MergeSubtitlesResponse
                {
                    Success = true,
                    Message = $"Fusion réussie : {merged.Count} répliques.",
                    OutputPath = outputPath,
                    CueCount = merged.Count
                };
            }
            catch (Exception ex)
            {
                return Fail($"Erreur de fusion : {ex.Message}");
            }
        }

        private static void ApplyOffset(List<SubtitleCue> cues, int offsetMs)
        {
            if (offsetMs == 0) return;

            var offset = TimeSpan.FromMilliseconds(offsetMs);
            foreach (var cue in cues)
            {
                cue.StartTime = cue.StartTime.Add(offset);
                cue.EndTime = cue.EndTime.Add(offset);
            }
        }
    }
}
