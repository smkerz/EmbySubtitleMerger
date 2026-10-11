using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Ce que la normalisation doit faire
    /// </summary>
    public class NormalizeOptions
    {
        /// <summary>true = aperçu : rien n'est écrit, renommé ni modifié</summary>
        public bool DryRun { get; set; } = true;

        /// <summary>Limite le traitement à cette vidéo. Null = tous les dossiers fournis</summary>
        public string? VideoPath { get; set; }

        /// <summary>Codes ISO 639-1 des langues à convertir (ex: en, fr, ru)</summary>
        public HashSet<string> Languages { get; set; } = new HashSet<string>();

        /// <summary>Refaire les .srt qui existent déjà</summary>
        public bool Overwrite { get; set; }

        /// <summary>Chemin de mkvpropedit, pour retirer la note du titre des pistes des .mkv (CleanupNote)</summary>
        public string? MkvPropEditPath { get; set; }

        /// <summary>
        /// Mode nettoyage : au lieu de normaliser, retire les copies de pistes texte intégrées,
        /// et ce que demandent les options Cleanup* ci-dessous.
        /// </summary>
        public bool Cleanup { get; set; }

        /// <summary>
        /// En mode nettoyage, retirer aussi tous les autres .srt tirés d'une piste de la vidéo ou d'un .ass :
        /// conversions de pistes ASS/SSA et fichiers laissés par l'ancien script.
        /// </summary>
        public bool CleanupConversions { get; set; }

        /// <summary>En mode nettoyage, retirer aussi les bilingues créés par la fusion (Film.Français + English.fr.srt, Film.dual.srt)</summary>
        public bool CleanupBilingual { get; set; }

        /// <summary>
        /// En mode nettoyage, retirer aussi la note "Normalisé pour Emby" du titre des pistes des .mkv
        /// (avec mkvpropedit) et du nom des .srt d'origine qui avaient été renommés.
        /// </summary>
        public bool CleanupNote { get; set; }

        /// <summary>
        /// Mode fusion en série : au lieu de normaliser, crée le bilingue BatchLanguage1 + BatchLanguage2
        /// de chaque vidéo qui a les deux langues et pas encore ce bilingue.
        /// </summary>
        public bool BatchMerge { get; set; }

        public LanguageInfo? BatchLanguage1 { get; set; }
        public LanguageInfo? BatchLanguage2 { get; set; }
        public MergeOptions BatchMergeOptions { get; set; } = new MergeOptions();

        /// <summary>En fusion en série, marquer les bilingues "default"</summary>
        public bool MarkDefault { get; set; }

        /// <summary>Avec VideoPath : traiter toutes les vidéos de son dossier (une saison), pas elle seule</summary>
        public bool WholeFolder { get; set; }

        /// <summary>Les modes qui ne parcourent que les vidéos, sans convertir les .ass/.ssa posés à côté</summary>
        public bool VideosOnly => Cleanup || BatchMerge;
    }

    /// <summary>
    /// Une opération prévue ou effectuée sur un fichier
    /// </summary>
    public class NormalizeAction
    {
        public const string Convert = "Convert";
        public const string Rename = "Rename";
        public const string Tag = "Tag";
        public const string Delete = "Delete";
        public const string Merge = "Merge";
        public const string Info = "Info";

        public const string Planned = "Planned";
        public const string Done = "Done";
        public const string Failed = "Failed";

        public string? Kind { get; set; }
        public string? Status { get; set; }

        /// <summary>Fichier d'origine (vidéo ou sous-titre)</summary>
        public string? Source { get; set; }

        /// <summary>Nom du fichier produit, ou nouveau titre de piste</summary>
        public string? Target { get; set; }

        public string? Detail { get; set; }
    }

    /// <summary>
    /// Tâche de normalisation
    /// </summary>
    public class NormalizeJob : BackgroundJob
    {
        /// <summary>Au-delà, les opérations ne sont plus listées une à une (les compteurs restent exacts)</summary>
        private const int MaxListedActions = 500;

        private readonly List<NormalizeAction> _actions = new List<NormalizeAction>();

        public bool DryRun { get; set; }

        /// <summary>true pour un nettoyage, false pour une normalisation</summary>
        public bool Cleanup { get; set; }

        /// <summary>true pour une fusion en série</summary>
        public bool BatchMerge { get; set; }

        /// <summary>Bilingues créés par une fusion en série</summary>
        public int Merged { get; private set; }

        /// <summary>Vidéos écartées d'une fusion en série, faute d'avoir les deux langues</summary>
        public int Unmatched { get; private set; }

        public int Created { get; private set; }
        public int Renamed { get; private set; }
        public int Tagged { get; private set; }
        public int Deleted { get; private set; }
        public int Failures { get; private set; }


        public int Changes => Created + Renamed + Tagged + Deleted + Merged;

        /// <summary>true si des opérations n'ont pas été listées (voir MaxListedActions)</summary>
        public bool ActionsTruncated { get; private set; }

        public void Add(string kind, string status, string source, string? target, string? detail = null)
        {
            lock (_actions)
            {
                if (status == NormalizeAction.Failed) Failures++;
                else if (kind == NormalizeAction.Convert) Created++;
                else if (kind == NormalizeAction.Rename) Renamed++;
                else if (kind == NormalizeAction.Tag) Tagged++;
                else if (kind == NormalizeAction.Delete) Deleted++;
                else if (kind == NormalizeAction.Merge) Merged++;

                if (_actions.Count >= MaxListedActions)
                {
                    ActionsTruncated = true;
                }
                else
                {
                    _actions.Add(new NormalizeAction
                    {
                        Kind = kind,
                        Status = status,
                        Source = source,
                        Target = target,
                        Detail = detail
                    });
                }
            }
        }

        public void CountUnmatched()
        {
            lock (_actions) Unmatched++;
        }

        public List<NormalizeAction> SnapshotActions()
        {
            lock (_actions) return _actions.ToList();
        }
    }

    /// <summary>
    /// Rend les sous-titres lisibles sur tous les appareils : les pistes ASS/SSA, que la plupart
    /// des TV et applis mobiles n'affichent pas, sont converties en .srt à côté des vidéos.
    /// Reprend les étapes du script emby_subs.py. L'image et le son des vidéos ne sont jamais réécrits ;
    /// seule l'option CleanupNote modifie l'en-tête des .mkv (titre des pistes).
    /// </summary>
    public static class SubtitleNormalizer
    {
        /// <summary>
        /// À quelle vidéo appartient chaque sous-titre d'un dossier. Calculé une fois par sous-titre :
        /// dans un dossier à plat de plusieurs centaines de films, le recalcul à chaque question
        /// coûterait des centaines de millions de comparaisons.
        /// </summary>
        private sealed class OwnerIndex
        {
            // Nom le plus long d'abord : "Film.2." l'emporte sur "Film."
            private readonly List<(string Prefix, string Video)> _prefixes;
            private readonly Dictionary<string, string?> _owners = new Dictionary<string, string?>(StringComparer.Ordinal);

            public OwnerIndex(IEnumerable<string> videos)
            {
                _prefixes = videos
                    .Select(v => (Prefix: Path.GetFileNameWithoutExtension(v) + ".", Video: v))
                    .OrderByDescending(e => e.Prefix.Length)
                    .ToList();
            }

            public string? Of(string subtitlePath)
            {
                if (_owners.TryGetValue(subtitlePath, out var owner)) return owner;

                var name = Path.GetFileName(subtitlePath);
                owner = _prefixes.FirstOrDefault(e => name.StartsWith(e.Prefix, StringComparison.Ordinal)).Video;
                _owners[subtitlePath] = owner;
                return owner;
            }
        }

        public static async Task RunAsync(
            NormalizeJob job,
            IReadOnlyList<string> folders,
            NormalizeOptions options,
            Action? afterChanges = null)
        {
            var cancellationToken = job.Cancellation.Token;

            try
            {
                job.Status = JobStatus.Running;
                job.Message = "Inventaire des fichiers…";

                var directories = ListDirectories(folders, options);

                // Seule la normalisation traite aussi les .ass/.ssa posés à côté des vidéos
                job.Total = directories.Sum(d => d.Count(f =>
                    InScope(f, d, options) && (!options.VideosOnly || NormalizeNaming.IsVideo(f))));

                foreach (var files in directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ProcessDirectoryAsync(job, files, options, cancellationToken);
                }

                if (options.BatchMerge)
                {
                    job.Message = $"{job.Merged} bilingue(s) {(options.DryRun ? "à créer" : "créé(s)")}, "
                        + $"{job.Unmatched} vidéo(s) sans les deux langues, {job.Failures} échec(s).";
                }
                else if (options.Cleanup)
                {
                    job.Message = $"{job.Deleted} fichier(s) {(options.DryRun ? "à supprimer" : "supprimé(s)")}, "
                        + $"{job.Renamed + job.Tagged} note(s) {(options.DryRun ? "à retirer" : "retirée(s)")}, {job.Failures} échec(s).";
                }
                else
                {
                    var verb = options.DryRun ? "à créer" : "créé(s)";
                    job.Message = $"{job.Created} .srt {verb}, {job.Renamed} renommage(s), {job.Failures} échec(s).";
                }

                // Emby doit rescanner pour voir les nouveaux fichiers
                if (!options.DryRun && job.Changes > 0)
                {
                    try { afterChanges?.Invoke(); } catch { }
                }

                job.Status = JobStatus.Completed;
                Plugin.Log($"[Normalize] {(options.DryRun ? "Aperçu" : "Terminé")}: {job.Message}");
            }
            catch (OperationCanceledException)
            {
                job.Status = JobStatus.Cancelled;
                job.Message = options.BatchMerge ? "Fusion en série annulée." : options.Cleanup ? "Nettoyage annulé." : "Normalisation annulée.";
            }
            catch (Exception ex)
            {
                job.Status = JobStatus.Failed;
                job.Error = ex.Message;
                job.Message = options.BatchMerge ? "Échec de la fusion en série." : options.Cleanup ? "Échec du nettoyage." : "Échec de la normalisation.";
                Plugin.Log($"[Normalize] ERREUR: {ex}");
            }
            finally
            {
                job.FinishedAt = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Fichiers utiles (vidéos et sous-titres), regroupés par dossier
        /// </summary>
        private static List<List<string>> ListDirectories(IReadOnlyList<string> folders, NormalizeOptions options)
        {
            IEnumerable<string> files;

            if (options.VideoPath != null)
            {
                files = Directory.EnumerateFiles(Path.GetDirectoryName(options.VideoPath) ?? ".");
            }
            else
            {
                var enumeration = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                files = folders
                    .Where(Directory.Exists)
                    .SelectMany(folder => Directory.EnumerateFiles(folder, "*", enumeration));
            }

            return files
                .Where(f => NormalizeNaming.IsVideo(f) || NormalizeNaming.IsSrt(f) || NormalizeNaming.IsStyledSubtitleFile(f))
                .Distinct(StringComparer.Ordinal)
                .GroupBy(f => Path.GetDirectoryName(f) ?? ".", StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList())
                .ToList();
        }

        /// <summary>
        /// Un fichier est à traiter si c'est une vidéo ou un .ass/.ssa, et, quand une seule vidéo
        /// est demandée, s'il s'agit de cette vidéo ou d'un sous-titre qui lui appartient.
        /// </summary>
        private static bool InScope(string file, List<string> directoryFiles, NormalizeOptions options)
        {
            var isVideo = NormalizeNaming.IsVideo(file);
            if (!isVideo && !NormalizeNaming.IsStyledSubtitleFile(file)) return false;
            if (options.VideoPath == null || options.WholeFolder) return true;

            var owner = isVideo ? file : NormalizeNaming.FindVideoFor(file, directoryFiles.Where(NormalizeNaming.IsVideo));
            return owner != null && SubtitleNaming.SamePath(owner, options.VideoPath);
        }

        private static async Task ProcessDirectoryAsync(
            NormalizeJob job,
            List<string> files,
            NormalizeOptions options,
            CancellationToken cancellationToken)
        {
            var videos = files.Where(NormalizeNaming.IsVideo).ToList();
            var owners = new OwnerIndex(videos);

            foreach (var video in videos.Where(v => InScope(v, files, options)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                job.Message = Path.GetFileName(video);

                try
                {
                    if (options.BatchMerge)
                    {
                        var current = video;
                        await BatchMerger.MergeVideoAsync(
                            job, current,
                            files.Where(f => NormalizeNaming.IsSrt(f) && owners.Of(f) == current).ToList(),
                            () => ProbeAsync(job, current, cancellationToken),
                            options, cancellationToken);
                    }
                    else if (options.Cleanup)
                    {
                        await CleanupVideoAsync(job, video, files, owners, options, cancellationToken);
                    }
                    else
                    {
                        MigrateLegacyNames(job, video, files, options);
                        await ProcessVideoAsync(job, video, files, owners, options, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Une vidéo en erreur n'arrête pas le parcours de toute la médiathèque
                    job.Add(NormalizeAction.Info, NormalizeAction.Failed, video, null, $"erreur inattendue : {ex.Message}");
                    Plugin.Log($"[Normalize] ERREUR sur {video}: {ex}");
                }

                job.Processed++;
            }

            if (options.VideosOnly) return;

            foreach (var subtitle in files.Where(NormalizeNaming.IsStyledSubtitleFile).Where(f => InScope(f, files, options)).ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                job.Message = Path.GetFileName(subtitle);

                // La note s'insère après le nom de la vidéo ; sans vidéo, simple changement d'extension
                var video = owners.Of(subtitle);
                var target = video != null
                    ? NormalizeNaming.PathWithNote(subtitle, video, ".srt")
                    : Path.ChangeExtension(subtitle, ".srt");

                // Sans vidéo, le nom ne porte pas la note : un .srt déjà là peut être celui de l'utilisateur
                if (video == null && File.Exists(target))
                {
                    job.Processed++;
                    continue;
                }

                await ConvertAsync(job, NormalizeAction.Convert, subtitle, null, target, "fichier externe", options, cancellationToken);
                job.Processed++;
            }
        }

        /// <summary>
        /// Nettoie une vidéo. Un .srt n'est supprimé que si son nom montre qu'il a été tiré d'une piste
        /// de la vidéo ou d'un .ass voisin : il peut alors être recréé. Les .srt d'origine renommés avec
        /// la note ne sont jamais supprimés ; CleanupNote leur rend leur nom. Les bilingues ne sont
        /// retirés que sur demande (CleanupBilingual) : il faut refaire la fusion pour les retrouver.
        /// </summary>
        private static async Task CleanupVideoAsync(
            NormalizeJob job,
            string video,
            List<string> files,
            OwnerIndex owners,
            NormalizeOptions options,
            CancellationToken cancellationToken)
        {
            var srts = files.Where(f => NormalizeNaming.IsSrt(f) && owners.Of(f) == video).ToList();
            var noted = srts.Where(NormalizeNaming.HasNote).ToList();

            // La vidéo n'est lue qu'au besoin, et une seule fois
            var probed = false;
            List<SubtitleTrack>? tracks = null;
            async Task<List<SubtitleTrack>?> TracksAsync()
            {
                if (!probed)
                {
                    probed = true;
                    tracks = await ProbeAsync(job, video, cancellationToken);
                }
                return tracks;
            }

            var toDelete = new Dictionary<string, string>(StringComparer.Ordinal);

            if (options.CleanupBilingual)
            {
                foreach (var srt in srts.Where(f => NormalizeNaming.IsBilingual(f, video)))
                    toDelete[srt] = "sous-titre bilingue";
            }

            foreach (var srt in noted.Where(f => NormalizeNaming.IsExtractedCopy(f, video)))
                toDelete[srt] = "copie d'une piste texte intégrée (en double dans Emby)";

            if (options.CleanupConversions)
            {
                // Note suivie d'une parenthèse : toujours un fichier tiré d'une piste de la vidéo
                foreach (var srt in noted.Where(f => NormalizeNaming.IsFromEmbeddedTrack(f, video) && !toDelete.ContainsKey(f)))
                    toDelete[srt] = "sous-titre tiré d'une piste de la vidéo (conversion ASS/SSA ou ancien script)";

                var notedNames = new HashSet<string>(noted, StringComparer.Ordinal);

                // Seule conversion de piste sans parenthèse : la première piste ASS/SSA d'une langue,
                // quand elle n'a pas de titre. Son nom est alors toujours "default".
                if (noted.Any(f => !toDelete.ContainsKey(f)) && NormalizeNaming.IsMkv(video))
                {
                    var seen = new HashSet<string>();
                    foreach (var track in (await TracksAsync() ?? new List<SubtitleTrack>()).Where(t => t.IsStyled))
                    {
                        if (!seen.Add(track.Language) || track.TitleForFile.Length > 0) continue;

                        var expected = NormalizeNaming.ConvertedPath(video, track.Language, 1, string.Empty, true);
                        if (notedNames.Contains(expected))
                            toDelete[expected] = $"conversion de la piste {track.Codec.ToUpperInvariant()} {track.Language}";
                    }
                }

                foreach (var styled in files.Where(f => NormalizeNaming.IsStyledSubtitleFile(f) && owners.Of(f) == video))
                {
                    var expected = NormalizeNaming.PathWithNote(styled, video, ".srt");
                    if (notedNames.Contains(expected))
                        toDelete[expected] = $"conversion de {Path.GetFileName(styled)}";
                }
            }

            foreach (var entry in toDelete.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (options.DryRun)
                {
                    job.Add(NormalizeAction.Delete, NormalizeAction.Planned, entry.Key, null, entry.Value);
                    continue;
                }

                try
                {
                    File.Delete(entry.Key);
                    files.Remove(entry.Key);
                    job.Add(NormalizeAction.Delete, NormalizeAction.Done, entry.Key, null, entry.Value);
                }
                catch (Exception ex)
                {
                    job.Add(NormalizeAction.Delete, NormalizeAction.Failed, entry.Key, null, ex.Message);
                }
            }

            if (!options.CleanupNote) return;

            RemoveNoteFromFiles(job, video, noted.Where(f => !toDelete.ContainsKey(f)).ToList(), files, owners, options);

            if (string.IsNullOrEmpty(options.MkvPropEditPath) || !NormalizeNaming.IsMkv(video)) return;

            foreach (var track in await TracksAsync() ?? new List<SubtitleTrack>())
            {
                var title = NormalizeNaming.TrackTitleWithoutNote(track.Title);
                if (title != null)
                    await SetTrackTitleAsync(job, video, track, title, options, cancellationToken);
            }
        }

        /// <summary>
        /// Les .srt d'origine renommés avec la note reprennent leur nom. Aucun fichier n'est supprimé.
        /// </summary>
        private static void RemoveNoteFromFiles(
            NormalizeJob job, string video, List<string> notedSrts, List<string> files, OwnerIndex owners, NormalizeOptions options)
        {
            // La conversion d'un .ass posé à côté de la vidéo garde la note : sans elle,
            // la normalisation ne la reconnaîtrait plus et la referait
            var externalConversions = new HashSet<string>(
                files.Where(f => NormalizeNaming.IsStyledSubtitleFile(f) && owners.Of(f) == video)
                    .Select(f => NormalizeNaming.PathWithNote(f, video, ".srt")),
                StringComparer.Ordinal);

            foreach (var srt in notedSrts)
            {
                var target = NormalizeNaming.PathWithoutNote(srt, video);
                if (target == null || externalConversions.Contains(srt)) continue;

                if (File.Exists(target))
                {
                    job.Add(NormalizeAction.Info, NormalizeAction.Planned, srt, null,
                        $"note laissée ({Path.GetFileName(target)} existe déjà)");
                    continue;
                }

                RenameFile(job, srt, target, "retrait de la note", files, options);
            }
        }

        /// <summary>
        /// Les .srt créés avec l'ancienne note à espaces prennent le nom actuel
        /// </summary>
        private static void MigrateLegacyNames(NormalizeJob job, string video, List<string> files, NormalizeOptions options)
        {
            foreach (var srt in files.Where(NormalizeNaming.IsSrt).ToList())
            {
                var target = NormalizeNaming.MigratedPath(srt, video);
                if (target == null || File.Exists(target)) continue;

                RenameFile(job, srt, target, "ancienne note à espaces", files, options);
            }
        }

        private static async Task ProcessVideoAsync(
            NormalizeJob job,
            string video,
            List<string> files,
            OwnerIndex owners,
            NormalizeOptions options,
            CancellationToken cancellationToken)
        {
            // Seul le .mkv peut contenir de l'ASS/SSA : les autres conteneurs ne sont pas lus
            var tracks = NormalizeNaming.IsMkv(video)
                ? await ProbeAsync(job, video, cancellationToken)
                : new List<SubtitleTrack>();

            // Vidéo illisible : on ne la prend pas pour une vidéo sans piste
            if (tracks == null) return;

            // Langues qui ont déjà une piste texte dans la vidéo, lisible sur tous les appareils
            var languagesWithText = new HashSet<string>(tracks.Where(t => t.IsText).Select(t => LanguageKey(t.Language)));

            // Pistes à sortir de la vidéo : elles le seront toutes ensemble, en une seule lecture
            var conversions = new List<PendingConversion>();

            // Pistes ASS/SSA intégrées : converties dans les langues choisies, sauf si la langue a déjà une piste texte
            var ranks = new Dictionary<string, int>();
            var defaultGiven = new HashSet<string>();
            foreach (var track in tracks.Where(t => t.IsStyled))
            {
                ranks[track.Language] = ranks.TryGetValue(track.Language, out var seen) ? seen + 1 : 1;


                if (!IsWanted(track.Language, options))
                {
                    job.Add(NormalizeAction.Info, NormalizeAction.Planned, video, null, $"piste ignorée (langue : {track.Language})");
                    continue;
                }

                if (languagesWithText.Contains(LanguageKey(track.Language)))
                {
                    job.Add(NormalizeAction.Info, NormalizeAction.Planned, video, null,
                        $"piste {track.Codec.ToUpperInvariant()} {track.Language} non convertie : la vidéo a déjà un sous-titre texte dans cette langue");
                    continue;
                }

                // "default" va à la première piste complète de la langue, jamais à une piste forced
                var isDefault = !track.IsForced && defaultGiven.Add(track.Language);
                var target = NormalizeNaming.ConvertedPath(video, track.Language, ranks[track.Language], track.TitleForFile, isDefault);
                var otherName = NormalizeNaming.ConvertedPath(video, track.Language, ranks[track.Language], track.TitleForFile, !isDefault);

                // Sans parenthèse, l'autre nom peut être un .srt d'origine annoté : on n'y touche pas
                if (!File.Exists(target) && File.Exists(otherName) && NormalizeNaming.IsFromEmbeddedTrack(otherName, video))
                {
                    // Fichier créé par une version qui marquait "default" la première piste, même forced
                    RenameFile(job, otherName, target,
                        isDefault ? "devient le sous-titre par défaut de la langue" : "piste forced : n'est plus le sous-titre par défaut", files, options);
                    continue;
                }

                conversions.Add(new PendingConversion(NormalizeAction.Convert, track.Index, target, $"piste {track.Codec.ToUpperInvariant()} {track.Language}"));
            }

            await ConvertTracksAsync(job, video, conversions, options, cancellationToken);
        }

        /// <summary>
        /// Une piste est retenue si sa langue fait partie de celles demandées.
        /// Les pistes sans langue ou dans une autre langue sont ignorées.
        /// </summary>
        private static bool IsWanted(string language, NormalizeOptions options)
        {
            var info = SubtitleNaming.Resolve(language);
            return info != null && options.Languages.Contains(info.Code);
        }

        /// <summary>Même clé pour "rus" et "ru" ; les langues inconnues gardent leur code</summary>
        private static string LanguageKey(string language)
        {
            return SubtitleNaming.Resolve(language)?.Code ?? language.ToLowerInvariant();
        }

        /// <summary>Pistes de sous-titres de la vidéo, ou null si elle n'a pas pu être lue</summary>
        internal static async Task<List<SubtitleTrack>?> ProbeAsync(NormalizeJob job, string video, CancellationToken cancellationToken)
        {
            var result = await ExternalTools.RunAsync(FfmpegHelper.FfprobePath, new[]
            {
                "-v", "error", "-select_streams", "s",
                "-show_entries", "stream=index,codec_name:stream_tags=language,title:stream_disposition=forced",
                "-of", "json", video
            }, 300, cancellationToken);

            if (!result.Success)
            {
                job.Add(NormalizeAction.Info, NormalizeAction.Failed, video, null, $"lecture impossible : {result.Summary}");
                return null;
            }

            try
            {
                return NormalizeNaming.ParseTracks(result.Output);
            }
            catch (Exception ex)
            {
                job.Add(NormalizeAction.Info, NormalizeAction.Failed, video, null, $"lecture impossible : {ex.Message}");
                return null;
            }
        }

        /// <summary>Une piste d'une vidéo à convertir en .srt</summary>
        private sealed class PendingConversion
        {
            public PendingConversion(string kind, int streamIndex, string output, string detail)
            {
                Kind = kind;
                StreamIndex = streamIndex;
                Output = output;
                Detail = detail;
            }

            public string Kind { get; }
            public int StreamIndex { get; }
            public string Output { get; }
            public string Detail { get; }
        }

        /// <summary>
        /// Convertit plusieurs pistes d'une vidéo en un seul passage de ffmpeg : la vidéo n'est lue
        /// qu'une fois, quel que soit le nombre de pistes. Si le passage groupé échoue (une piste
        /// illisible suffit), on recommence piste par piste pour sauver les autres et nommer la fautive.
        /// </summary>
        private static async Task ConvertTracksAsync(
            NormalizeJob job,
            string video,
            List<PendingConversion> conversions,
            NormalizeOptions options,
            CancellationToken cancellationToken)
        {
            // Mêmes règles que pour une piste seule : un .srt déjà présent n'est pas refait, l'aperçu n'écrit rien
            var todo = conversions.Where(c => options.Overwrite || !File.Exists(c.Output)).ToList();

            if (!options.DryRun && todo.Count > 1)
            {
                var grouped = await ExternalTools.ConvertToSrtAsync(
                    video, todo.Select(c => ((int?)c.StreamIndex, c.Output)).ToList(), cancellationToken);

                if (grouped.Success)
                {
                    foreach (var done in todo)
                        job.Add(done.Kind, NormalizeAction.Done, video, Path.GetFileName(done.Output), done.Detail);
                    return;
                }
            }

            foreach (var conversion in todo)
                await ConvertAsync(job, conversion.Kind, video, conversion.StreamIndex, conversion.Output, conversion.Detail, options, cancellationToken);
        }

        /// <summary>
        /// Convertit une piste (ou un fichier .ass/.ssa si streamIndex est null) en .srt nettoyé
        /// </summary>
        private static async Task ConvertAsync(
            NormalizeJob job,
            string kind,
            string input,
            int? streamIndex,
            string output,
            string detail,
            NormalizeOptions options,
            CancellationToken cancellationToken)
        {
            // Un .srt déjà présent n'est pas refait, sauf demande explicite
            if (File.Exists(output) && !options.Overwrite) return;

            var name = Path.GetFileName(output);

            if (options.DryRun)
            {
                job.Add(kind, NormalizeAction.Planned, input, name, detail);
                return;
            }

            var result = await ExternalTools.ConvertToSrtAsync(input, streamIndex, output, cancellationToken);

            if (result.Success)
                job.Add(kind, NormalizeAction.Done, input, name, detail);
            else
                job.Add(kind, NormalizeAction.Failed, input, name, result.Summary);
        }

        private static void RenameFile(NormalizeJob job, string source, string target, string detail, List<string> files, NormalizeOptions options)
        {
            var name = Path.GetFileName(target);

            if (options.DryRun)
            {
                job.Add(NormalizeAction.Rename, NormalizeAction.Planned, source, name, detail);
                return;
            }

            try
            {
                File.Move(source, target);

                // La liste du dossier suit le renommage : les étapes suivantes voient le nouveau nom
                files.Remove(source);
                files.Add(target);

                job.Add(NormalizeAction.Rename, NormalizeAction.Done, source, name, detail);
            }
            catch (Exception ex)
            {
                job.Add(NormalizeAction.Rename, NormalizeAction.Failed, source, name, ex.Message);
            }
        }

        /// <summary>
        /// Change le titre d'une piste de sous-titres d'un .mkv ; un titre vide le supprime
        /// </summary>
        private static async Task SetTrackTitleAsync(
            NormalizeJob job,
            string video,
            SubtitleTrack track,
            string title,
            NormalizeOptions options,
            CancellationToken cancellationToken)
        {
            var detail = $"sous-titre {track.Number} ({track.Language})";
            var shown = title.Length > 0 ? title : "(sans titre)";

            if (options.DryRun)
            {
                job.Add(NormalizeAction.Tag, NormalizeAction.Planned, video, shown, detail);
                return;
            }

            var edit = title.Length > 0 ? new[] { "--set", $"name={title}" } : new[] { "--delete", "name" };
            var result = await ExternalTools.RunAsync(options.MkvPropEditPath!,
                new[] { video, "--edit", $"track:s{track.Number}" }.Concat(edit).ToArray(), 120, cancellationToken);

            title = shown;

            if (result.Success)
                job.Add(NormalizeAction.Tag, NormalizeAction.Done, video, title, detail);
            else
                job.Add(NormalizeAction.Tag, NormalizeAction.Failed, video, title, result.Summary);
        }
    }
}
