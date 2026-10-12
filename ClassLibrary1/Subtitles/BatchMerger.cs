using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Fusion en série : crée le bilingue de chaque vidéo d'un dossier ou d'une médiathèque
    /// à partir de deux langues, en choisissant lui-même la piste de chaque langue.
    /// Sur demande, une langue absente est obtenue en traduisant un sous-titre qui existe.
    /// </summary>
    public static class BatchMerger
    {
        /// <summary>Mots du nom d'un .srt qui ne sont pas un code de langue</summary>
        private static readonly HashSet<string> NameFlags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "default", "forced", "sdh", "cc", "hi" };

        /// <summary>
        /// Après ce nombre d'échecs de traduction d'affilée, le moteur est considéré comme indisponible
        /// (quota atteint, clé refusée…) : inutile de le solliciter pour chaque vidéo restante.
        /// </summary>
        private const int MaxTranslationFailuresInARow = 2;

        /// <summary>Une piste retenue pour la fusion</summary>
        internal sealed class Candidate
        {
            public bool IsExternal { get; set; }
            public string? Path { get; set; }
            public int Index { get; set; }

            /// <summary>Langue de la piste, si elle est connue</summary>
            public LanguageInfo? Language { get; set; }

            /// <summary>Traduction automatique d'une série précédente : jamais préférée comme texte à traduire</summary>
            public bool IsAutoTranslation { get; set; }

            /// <summary>Description pour le compte rendu</summary>
            public string Label { get; set; } = string.Empty;
        }

        /// <summary>
        /// Langue d'un .srt posé à côté d'une vidéo, lue comme le fait Emby : le dernier mot du nom
        /// avant ".srt", une fois écartés "default", "forced", etc.
        /// </summary>
        internal static (LanguageInfo? Language, bool Forced) DescribeExternal(string srtPath, string videoPath)
        {
            var stem = Path.GetFileNameWithoutExtension(videoPath);
            var name = Path.GetFileName(srtPath);

            if (!name.StartsWith(stem + ".", StringComparison.Ordinal))
                return (null, false);

            // Tout se lit après le nom de la vidéo : "Forced.Vengeance.2018.en.srt" n'est pas une piste forced
            var suffix = name.Substring(stem.Length + 1);
            var forced = suffix.Contains("forced", StringComparison.OrdinalIgnoreCase);

            var words = suffix.Split('.');
            for (var i = words.Length - 2; i >= 0; i--)
            {
                // "hi" est le hindi, sauf derrière un code de langue où il veut dire "malentendants" : Film.en.hi.srt
                var isFlag = words[i].Equals("hi", StringComparison.OrdinalIgnoreCase)
                    ? i > 0 && SubtitleNaming.Resolve(words[i - 1]) != null
                    : NameFlags.Contains(words[i]);
                if (isFlag) continue;

                return (SubtitleNaming.Resolve(words[i]), forced);
            }

            return (null, forced);
        }

        /// <summary>
        /// Les pistes utilisables d'une vidéo, de la meilleure à la moins bonne : les .srt d'origine d'abord
        /// (rien à extraire), puis les pistes texte de la vidéo, puis les pistes ASS/SSA.
        /// Les pistes forced, les images et les bilingues sont écartés.
        /// </summary>
        private static IEnumerable<Candidate> Usable(string video, IEnumerable<string> ownedSrts, IEnumerable<SubtitleTrack>? tracks)
        {
            var externals = ownedSrts
                .Where(srt => !NormalizeNaming.IsBilingual(srt, video))
                .Select(srt => (Path: srt, Info: DescribeExternal(srt, video), Auto: NormalizeNaming.IsAutoTranslation(srt, video)))
                .Where(e => !e.Info.Forced)
                .OrderBy(e => e.Auto ? 2 : NormalizeNaming.HasNote(e.Path) ? 1 : 0)
                .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase);

            foreach (var external in externals)
            {
                yield return new Candidate
                {
                    IsExternal = true,
                    Path = external.Path,
                    // Un nom sans code de langue ("Film.srt") : la langue est lue dans le texte
                    Language = external.Info.Language ?? DetectLanguage(external.Path),
                    IsAutoTranslation = external.Auto,
                    Label = Path.GetFileName(external.Path)
                };
            }

            var embedded = (tracks ?? Enumerable.Empty<SubtitleTrack>())
                .Where(t => !t.IsForced && (t.IsText || t.IsStyled))
                .OrderBy(t => t.IsText ? 0 : 1)
                .ThenBy(t => t.Number);

            foreach (var track in embedded)
            {
                yield return new Candidate
                {
                    Index = track.Index,
                    Language = SubtitleNaming.Resolve(track.Language),
                    Label = $"piste {track.Number} ({track.Codec}{(track.Title.Length > 0 ? ", " + track.Title : string.Empty)})"
                };
            }
        }

        private static LanguageInfo? DetectLanguage(string srtPath)
        {
            try
            {
                return SubtitleNaming.Resolve(LanguageDetector.DetectSingle(SrtParser.ParseFile(srtPath)));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>La meilleure piste d'une langue, ou null si la vidéo n'en a pas</summary>
        internal static Candidate? Pick(LanguageInfo language, string video, IEnumerable<string> ownedSrts, IEnumerable<SubtitleTrack>? tracks)
        {
            return Usable(video, ownedSrts, tracks).FirstOrDefault(c => c.Language?.Code == language.Code);
        }

        /// <summary>
        /// Crée le bilingue d'une vidéo, s'il n'existe pas déjà. Sans l'option de traduction, il faut les deux
        /// langues ; avec elle, une langue absente est traduite depuis l'autre, et si les deux manquent,
        /// elles sont toutes deux traduites depuis le premier sous-titre disponible.
        /// </summary>
        internal static async Task MergeVideoAsync(
            NormalizeJob job,
            string video,
            IReadOnlyList<string> ownedSrts,
            Func<Task<List<SubtitleTrack>?>> probeAsync,
            NormalizeOptions options,
            CancellationToken cancellationToken)
        {
            var language1 = options.BatchLanguage1!;
            var language2 = options.BatchLanguage2!;

            var output = SubtitleNaming.DualPath(video, language1, language2, options.MarkDefault);
            var otherForm = SubtitleNaming.DualPath(video, language1, language2, !options.MarkDefault);

            // Déjà fait : relancer la série ne refait que les nouveaux films
            if ((File.Exists(output) || File.Exists(otherForm)) && !options.Overwrite) return;

            var first = Pick(language1, video, ownedSrts, null);
            var second = Pick(language2, video, ownedSrts, null);
            List<SubtitleTrack>? tracks = null;

            // La vidéo n'est lue que si les fichiers posés à côté ne suffisent pas
            if (first == null || second == null)
            {
                tracks = await probeAsync();
                if (tracks == null) return;

                first ??= Pick(language1, video, ownedSrts, tracks);
                second ??= Pick(language2, video, ownedSrts, tracks);
            }

            // Sous-titre à traduire pour obtenir la ou les langues absentes
            Candidate? source = null;
            if (first == null || second == null)
            {
                if (!options.BatchTranslate || job.TranslationHalted)
                {
                    job.CountUnmatched();
                    return;
                }

                // On traduit de préférence un sous-titre d'origine : traduire une traduction dégrade le texte
                var present = new[] { first, second }.Where(c => c != null).Select(c => c!).ToList();
                source = present.FirstOrDefault(c => !c.IsAutoTranslation)
                    ?? Usable(video, ownedSrts, tracks).FirstOrDefault(c => !c.IsAutoTranslation)
                    ?? present.FirstOrDefault()
                    ?? Usable(video, ownedSrts, tracks).FirstOrDefault();
                if (source == null)
                {
                    job.CountWithoutSubtitle();
                    job.Add(NormalizeAction.Info, NormalizeAction.Planned, video, null,
                        "aucun sous-titre texte, ni dans la vidéo ni à côté : rien à fusionner ni à traduire");
                    return;
                }
            }

            string Describe(LanguageInfo language, Candidate? candidate)
            {
                return candidate != null
                    ? $"{language.NativeName} : {candidate.Label}"
                    : $"{language.NativeName} : traduit depuis {source!.Language?.NativeName ?? "une langue non précisée"} ({source.Label})";
            }

            var name = Path.GetFileName(output);
            var detail = Describe(language1, first) + " + " + Describe(language2, second);

            if (options.DryRun)
            {
                // L'aperçu annonce les traductions qui seraient lancées : elles prennent du temps, et coûtent avec Claude
                if (source != null)
                {
                    if (first == null) job.CountTranslation();
                    if (second == null) job.CountTranslation();
                }

                job.Add(NormalizeAction.Merge, NormalizeAction.Planned, video, name, detail);
                return;
            }

            SubtitleSource source1;
            SubtitleSource? source2 = null;

            if (source == null)
            {
                (source1, source2) = await SubtitleSource.AcquirePairAsync(
                    video, first!.IsExternal, first.Path, first.Index, second!.IsExternal, second.Path, second.Index, cancellationToken);
            }
            else
            {
                source1 = await SubtitleSource.AcquireAsync(
                    video, source.IsExternal, source.Path, source.Index, "sous-titre à traduire", cancellationToken);
            }

            if (!source1.Success || source2?.Success == false)
            {
                job.Add(NormalizeAction.Merge, NormalizeAction.Failed, video, name, source1.Error ?? source2?.Error);
                return;
            }

            try
            {
                List<SubtitleCue> primary, secondary;

                if (source == null)
                {
                    primary = SrtParser.ParseFile(source1.Path);
                    secondary = SrtParser.ParseFile(source2!.Path);
                }
                else
                {
                    var cues = SrtParser.ParseFile(source1.Path);
                    if (cues.Count == 0)
                    {
                        job.Add(NormalizeAction.Merge, NormalizeAction.Failed, video, name, "le sous-titre à traduire est vide");
                        return;
                    }

                    // Répliques d'une langue : celles de la source si elle est dans cette langue ou si c'est
                    // la piste trouvée, sinon celles d'une autre piste trouvée, sinon une traduction de la source
                    async Task<List<SubtitleCue>> CuesForAsync(Candidate? found, LanguageInfo language)
                    {
                        if (found == source) return cues;
                        if (found != null) return await LoadAsync(video, found, cancellationToken);
                        return await TranslateAsync(job, video, cues, source.Language, language, options, cancellationToken);
                    }

                    try
                    {
                        primary = await CuesForAsync(first, language1);
                        secondary = await CuesForAsync(second, language2);
                        job.TranslationFailuresInARow = 0;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        job.Add(NormalizeAction.Merge, NormalizeAction.Failed, video, name, "traduction impossible : " + ex.Message);

                        // Seul un refus du moteur (quota, clé) annonce que les suivantes échoueront aussi ;
                        // un fichier illisible ou un dossier protégé ne concerne que cette vidéo
                        if (ex is TranslationEngineException && ++job.TranslationFailuresInARow >= MaxTranslationFailuresInARow)
                        {
                            job.TranslationHalted = true;
                            job.Add(NormalizeAction.Info, NormalizeAction.Failed, video, null,
                                "traductions suspendues pour le reste de la série : le moteur de traduction ne répond plus. "
                                + "Les vidéos qui ont déjà les deux langues continuent d'être fusionnées ; relancez plus tard pour les autres.");
                        }
                        return;
                    }
                }

                var merged = SubtitleMerger.Merge(primary, secondary, options.BatchMergeOptions);
                if (merged.Count == 0)
                {
                    job.Add(NormalizeAction.Merge, NormalizeAction.Failed, video, name, "aucune réplique à écrire");
                    return;
                }

                SrtParser.WriteFile(output, merged);

                // Le même bilingue sous l'autre forme (avec ou sans "default") ferait doublon dans Emby.
                // Ne pas réussir à le retirer ne fait pas échouer la fusion, qui est écrite.
                try { if (File.Exists(otherForm)) File.Delete(otherForm); } catch { }

                job.Add(NormalizeAction.Merge, NormalizeAction.Done, video, name, $"{detail} ({merged.Count} répliques)");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                job.Add(NormalizeAction.Merge, NormalizeAction.Failed, video, name, ex.Message);
            }
            finally
            {
                source1.Cleanup();
                source2?.Cleanup();
            }
        }

        /// <summary>
        /// Traduit un sous-titre dans une langue. La traduction est aussi gardée à côté de la vidéo
        /// ("Film.Traduction-auto(google).fr.srt") : elle sert de sous-titre à part entière, ne sera pas refaite
        /// la prochaine fois, et sa note empêche de la confondre avec un sous-titre d'origine.
        /// </summary>
        private static async Task<List<SubtitleCue>> TranslateAsync(
            NormalizeJob job,
            string video,
            List<SubtitleCue> cues,
            LanguageInfo? sourceLanguage,
            LanguageInfo target,
            NormalizeOptions options,
            CancellationToken cancellationToken)
        {
            job.Message = $"{Path.GetFileName(video)} : traduction en {target.NativeName}…";

            using var engine = options.CreateTranslationEngine!(sourceLanguage?.Code, target.Code);

            // Une série interrompue reprend la traduction en cours là où elle s'était arrêtée
            var checkpoint = TranslationCheckpoint.Open(cues, target.Code, options.TranslationEngineName);
            var translated = await SubtitleTranslator.TranslateCuesAsync(
                cues, engine, TranslationEngines.BatchSize(options.TranslationEngineName), null, cancellationToken, checkpoint);

            // Le point de reprise n'est effacé qu'une fois la traduction écrite : si le dossier refuse
            // l'écriture, relancer ne la repaie pas
            SrtParser.WriteFile(NormalizeNaming.AutoTranslationPath(video, target.Code, options.TranslationEngineName), translated);
            checkpoint.Delete();

            job.CountTranslation();
            return translated;
        }

        /// <summary>Répliques d'une piste retenue</summary>
        private static async Task<List<SubtitleCue>> LoadAsync(string video, Candidate candidate, CancellationToken cancellationToken)
        {
            var source = await SubtitleSource.AcquireAsync(
                video, candidate.IsExternal, candidate.Path, candidate.Index, "sous-titre", cancellationToken);
            if (!source.Success)
                throw new IOException(source.Error);

            try
            {
                return SrtParser.ParseFile(source.Path);
            }
            finally
            {
                source.Cleanup();
            }
        }
    }
}
