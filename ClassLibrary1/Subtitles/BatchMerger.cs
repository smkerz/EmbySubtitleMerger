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
    /// </summary>
    public static class BatchMerger
    {
        /// <summary>Mots du nom d'un .srt qui ne sont pas un code de langue</summary>
        private static readonly HashSet<string> NameFlags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "default", "forced", "sdh", "cc", "hi" };

        /// <summary>Une piste retenue pour la fusion</summary>
        internal sealed class Candidate
        {
            public bool IsExternal { get; set; }
            public string? Path { get; set; }
            public int Index { get; set; }

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
        /// La meilleure piste d'une langue : un .srt d'origine d'abord (rien à extraire), puis une piste
        /// texte de la vidéo, puis une piste ASS/SSA. Les pistes forced, les images et les bilingues sont écartés.
        /// </summary>
        internal static Candidate? Pick(LanguageInfo language, string video, IEnumerable<string> ownedSrts, IEnumerable<SubtitleTrack>? tracks)
        {
            var external = ownedSrts
                .Where(srt => !NormalizeNaming.IsBilingual(srt, video))
                .Select(srt => (Path: srt, Info: DescribeExternal(srt, video)))
                .Where(e => !e.Info.Forced && e.Info.Language?.Code == language.Code)
                .OrderBy(e => NormalizeNaming.HasNote(e.Path) ? 1 : 0)
                .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .Select(e => e.Path)
                .FirstOrDefault();

            if (external != null)
                return new Candidate { IsExternal = true, Path = external, Label = Path.GetFileName(external) };

            var track = (tracks ?? Enumerable.Empty<SubtitleTrack>())
                .Where(t => !t.IsForced && (t.IsText || t.IsStyled) && SubtitleNaming.Resolve(t.Language)?.Code == language.Code)
                .OrderBy(t => t.IsText ? 0 : 1)
                .ThenBy(t => t.Number)
                .FirstOrDefault();

            if (track == null) return null;

            return new Candidate
            {
                Index = track.Index,
                Label = $"piste {track.Number} ({track.Codec}{(track.Title.Length > 0 ? ", " + track.Title : string.Empty)})"
            };
        }

        /// <summary>
        /// Crée le bilingue d'une vidéo, s'il n'existe pas déjà et si les deux langues sont disponibles.
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

            // La vidéo n'est lue que si les fichiers posés à côté ne suffisent pas
            if (first == null || second == null)
            {
                var tracks = await probeAsync();
                if (tracks == null) return;

                first ??= Pick(language1, video, ownedSrts, tracks);
                second ??= Pick(language2, video, ownedSrts, tracks);
            }

            if (first == null || second == null)
            {
                job.CountUnmatched();
                return;
            }

            var name = Path.GetFileName(output);
            var detail = $"{language1.NativeName} : {first.Label} + {language2.NativeName} : {second.Label}";

            if (options.DryRun)
            {
                job.Add(NormalizeAction.Merge, NormalizeAction.Planned, video, name, detail);
                return;
            }

            var (source1, source2) = await SubtitleSource.AcquirePairAsync(
                video, first.IsExternal, first.Path, first.Index, second.IsExternal, second.Path, second.Index, cancellationToken);

            if (!source1.Success || !source2.Success)
            {
                job.Add(NormalizeAction.Merge, NormalizeAction.Failed, video, name, source1.Error ?? source2.Error);
                return;
            }

            try
            {
                var merged = SubtitleMerger.Merge(
                    SrtParser.ParseFile(source1.Path), SrtParser.ParseFile(source2.Path), options.BatchMergeOptions);

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
            catch (Exception ex)
            {
                job.Add(NormalizeAction.Merge, NormalizeAction.Failed, video, name, ex.Message);
            }
            finally
            {
                source1.Cleanup();
                source2.Cleanup();
            }
        }
    }
}
