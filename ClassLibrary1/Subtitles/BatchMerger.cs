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
            var forced = name.Contains("forced", StringComparison.OrdinalIgnoreCase);

            if (!name.StartsWith(stem + ".", StringComparison.Ordinal))
                return (null, forced);

            var words = name.Substring(stem.Length + 1).Split('.');
            for (var i = words.Length - 2; i >= 0; i--)
            {
                if (NameFlags.Contains(words[i])) continue;
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
                .Select(srt => (Path: srt, Info: DescribeExternal(srt, video)))
                .Where(e => !e.Info.Forced)
                .OrderBy(e => NormalizeNaming.HasNote(e.Path) ? 1 : 0)
                .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase);

            foreach (var external in externals)
            {
                yield return new Candidate
                {
                    IsExternal = true,
                    Path = external.Path,
                    Language = external.Info.Language,
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

                source = first ?? second ?? Usable(video, ownedSrts, tracks).FirstOrDefault();
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

                    try
                    {
                        primary = first != null ? cues : await TranslateAsync(job, video, cues, source.Language, language1, options, cancellationToken);
                        secondary = second != null ? cues : await TranslateAsync(job, video, cues, source.Language, language2, options, cancellationToken);
                        job.TranslationFailuresInARow = 0;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        job.Add(NormalizeAction.Merge, NormalizeAction.Failed, video, name, "traduction impossible : " + ex.Message);

                        if (++job.TranslationFailuresInARow >= MaxTranslationFailuresInARow)
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

                // Le même bilingue sous l'autre forme (avec ou sans "default") ferait doublon dans Emby
                if (File.Exists(otherForm)) File.Delete(otherForm);

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
        /// ("Film.fr.srt") : elle sert de sous-titre à part entière, et ne sera pas refaite la prochaine fois.
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
                cues, engine, SubtitleTranslator.DefaultBatchSize, null, cancellationToken, checkpoint);
            checkpoint.Delete();

            var single = SubtitleNaming.SinglePath(video, target);
            if (!File.Exists(single))
                SrtParser.WriteFile(single, translated);

            job.CountTranslation();
            return translated;
        }
    }
}
