using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Mode de fusion des sous-titres
    /// </summary>
    public enum MergeMode
    {
        /// <summary>Tous les cues des deux sous-titres</summary>
        AllCues,
        /// <summary>Seulement les cues qui se chevauchent</summary>
        OverlappingOnly,
        /// <summary>Priorité au sous-titre primaire</summary>
        PrimaryPriority
    }
    
    /// <summary>
    /// Disposition des deux langues à l'écran
    /// </summary>
    public enum DualLayout
    {
        /// <summary>Langue 1 en haut, langue 2 en bas : deux répliques distinctes</summary>
        TopBottom,
        /// <summary>Les deux langues l'une sous l'autre en bas, sans balise de position</summary>
        Stacked
    }

    /// <summary>
    /// Options de fusion
    /// </summary>
    public class MergeOptions
    {
        /// <summary>Mode de fusion</summary>
        public MergeMode Mode { get; set; } = MergeMode.AllCues;
        
        /// <summary>Tolérance en millisecondes pour considérer deux cues comme simultanés</summary>
        public int ToleranceMs { get; set; } = 700;
        
        /// <summary>Couleur du sous-titre primaire (langue 1) - format ASS/HTML</summary>
        public string? PrimaryColor { get; set; }

        /// <summary>Couleur du sous-titre secondaire (langue 2)</summary>
        public string? SecondaryColor { get; set; }

        /// <summary>Disposition des deux langues à l'écran</summary>
        public DualLayout Layout { get; set; } = DualLayout.TopBottom;

        /// <summary>
        /// Construit les options à partir des valeurs reçues d'une requête.
        /// Une tolérance absente garde la valeur par défaut.
        /// </summary>
        public static MergeOptions From(string? mode, int? toleranceMs, string? color1, string? color2, string? layout)
        {
            var options = new MergeOptions
            {
                Mode = SubtitleMerger.ParseMode(mode),
                Layout = SubtitleMerger.ParseLayout(layout),
                PrimaryColor = color1,
                SecondaryColor = color2
            };

            if (toleranceMs.HasValue)
                options.ToleranceMs = toleranceMs.Value;

            return options;
        }
    }
    
    /// <summary>
    /// Fusionne deux fichiers de sous-titres
    /// </summary>
    public static class SubtitleMerger
    {
        /// <summary>Balise placée en tête des répliques du haut en disposition haut/bas</summary>
        public const string TopPositionTag = "{\\an8}";

        /// <summary>
        /// Fusionne deux listes de sous-titres
        /// </summary>
        /// <param name="primary">Sous-titres primaires (langue 1, en haut en disposition haut/bas)</param>
        /// <param name="secondary">Sous-titres secondaires (langue 2)</param>
        /// <param name="options">Options de fusion</param>
        /// <returns>Liste fusionnée</returns>
        public static List<SubtitleCue> Merge(
            List<SubtitleCue> primary, 
            List<SubtitleCue> secondary, 
            MergeOptions? options = null)
        {
            options ??= new MergeOptions();

            if (options.Layout == DualLayout.TopBottom)
                return MergeTopBottom(primary, secondary, options);
            
            return options.Mode switch
            {
                MergeMode.AllCues => MergeAllCues(primary, secondary, options),
                MergeMode.OverlappingOnly => MergeOverlappingOnly(primary, secondary, options),
                MergeMode.PrimaryPriority => MergePrimaryPriority(primary, secondary, options),
                _ => MergeAllCues(primary, secondary, options)
            };
        }
        
        /// <summary>
        /// Aligne les timings des sous-titres primaires et secondaires
        /// pour éviter les micro-segments qui causent des sauts visuels.
        /// Si un cue secondaire commence/finit proche d'un cue primaire (dans la tolérance),
        /// on aligne les temps pour qu'ils soient synchronisés.
        /// </summary>
        private static void SnapTimings(
            List<SubtitleCue> primary,
            List<SubtitleCue> secondary,
            long snapToleranceTicks)
        {
            foreach (var sCue in secondary)
            {
                foreach (var pCue in primary)
                {
                    // Aligner les débuts proches
                    if (Math.Abs(sCue.StartTime.Ticks - pCue.StartTime.Ticks) <= snapToleranceTicks
                        && sCue.StartTime != pCue.StartTime)
                    {
                        sCue.StartTime = pCue.StartTime;
                    }

                    // Aligner les fins proches
                    if (Math.Abs(sCue.EndTime.Ticks - pCue.EndTime.Ticks) <= snapToleranceTicks
                        && sCue.EndTime != pCue.EndTime)
                    {
                        sCue.EndTime = pCue.EndTime;
                    }

                    // Aligner début secondaire sur fin primaire (et inversement)
                    if (Math.Abs(sCue.StartTime.Ticks - pCue.EndTime.Ticks) <= snapToleranceTicks
                        && sCue.StartTime != pCue.EndTime)
                    {
                        sCue.StartTime = pCue.EndTime;
                    }
                    if (Math.Abs(sCue.EndTime.Ticks - pCue.StartTime.Ticks) <= snapToleranceTicks
                        && sCue.EndTime != pCue.StartTime)
                    {
                        sCue.EndTime = pCue.StartTime;
                    }
                }
            }
        }

        /// <summary>
        /// Fusionne tous les cues des deux sous-titres
        /// </summary>
        private static List<SubtitleCue> MergeAllCues(
            List<SubtitleCue> primary,
            List<SubtitleCue> secondary,
            MergeOptions options)
        {
            var result = new List<SubtitleCue>();
            var toleranceTicks = TimeSpan.FromMilliseconds(options.ToleranceMs).Ticks;

            // Aligner les timings proches pour éviter les micro-segments
            // Utilise la moitié de la tolérance pour le snapping
            SnapTimings(primary, secondary, toleranceTicks / 2);

            // Créer des événements pour tous les points de changement
            var events = new List<(TimeSpan Time, bool IsStart, bool IsPrimary, SubtitleCue Cue)>();

            foreach (var cue in primary)
            {
                events.Add((cue.StartTime, true, true, cue));
                events.Add((cue.EndTime, false, true, cue));
            }

            foreach (var cue in secondary)
            {
                events.Add((cue.StartTime, true, false, cue));
                events.Add((cue.EndTime, false, false, cue));
            }

            // Trier par temps
            events = events.OrderBy(e => e.Time).ThenBy(e => e.IsStart ? 0 : 1).ToList();
            
            // Suivre les cues actifs
            SubtitleCue? activePrimary = null;
            SubtitleCue? activeSecondary = null;
            TimeSpan? segmentStart = null;
            
            foreach (var evt in events)
            {
                // Si on a des cues actifs et qu'on change d'état, créer un segment
                if (segmentStart.HasValue && (activePrimary != null || activeSecondary != null))
                {
                    if (evt.Time > segmentStart.Value)
                    {
                        var mergedCue = CreateMergedCue(
                            segmentStart.Value, 
                            evt.Time, 
                            activePrimary, 
                            activeSecondary, 
                            options);
                        
                        if (mergedCue != null)
                            result.Add(mergedCue);
                    }
                }
                
                // Mettre à jour l'état
                if (evt.IsStart)
                {
                    if (evt.IsPrimary)
                        activePrimary = evt.Cue;
                    else
                        activeSecondary = evt.Cue;
                }
                else
                {
                    if (evt.IsPrimary && activePrimary == evt.Cue)
                        activePrimary = null;
                    else if (!evt.IsPrimary && activeSecondary == evt.Cue)
                        activeSecondary = null;
                }
                
                segmentStart = evt.Time;
            }
            
            // Fusionner les segments adjacents identiques
            result = MergeAdjacentCues(result, toleranceTicks);
            
            // Renuméroter
            for (int i = 0; i < result.Count; i++)
                result[i].Index = i + 1;
            
            return result;
        }
        
        /// <summary>
        /// Fusionne seulement les cues qui se chevauchent
        /// </summary>
        private static List<SubtitleCue> MergeOverlappingOnly(
            List<SubtitleCue> primary,
            List<SubtitleCue> secondary,
            MergeOptions options)
        {
            var result = new List<SubtitleCue>();
            var toleranceTicks = TimeSpan.FromMilliseconds(options.ToleranceMs).Ticks;

            SnapTimings(primary, secondary, toleranceTicks / 2);
            
            foreach (var pCue in primary)
            {
                // Trouver les cues secondaires qui chevauchent
                var overlapping = secondary.Where(s => 
                    CuesOverlap(pCue, s, toleranceTicks)).ToList();
                
                if (overlapping.Any())
                {
                    // Combiner avec le premier qui chevauche
                    var sCue = overlapping.First();
                    var start = pCue.StartTime < sCue.StartTime ? pCue.StartTime : sCue.StartTime;
                    var end = pCue.EndTime > sCue.EndTime ? pCue.EndTime : sCue.EndTime;
                    
                    var merged = CreateMergedCue(start, end, pCue, sCue, options);
                    if (merged != null)
                        result.Add(merged);
                }
            }
            
            // Renuméroter
            result = result.OrderBy(c => c.StartTime).ToList();
            for (int i = 0; i < result.Count; i++)
                result[i].Index = i + 1;
            
            return result;
        }
        
        /// <summary>
        /// Fusionne avec priorité au primaire
        /// </summary>
        private static List<SubtitleCue> MergePrimaryPriority(
            List<SubtitleCue> primary,
            List<SubtitleCue> secondary,
            MergeOptions options)
        {
            var result = new List<SubtitleCue>();
            var toleranceTicks = TimeSpan.FromMilliseconds(options.ToleranceMs).Ticks;

            SnapTimings(primary, secondary, toleranceTicks / 2);
            
            // Ajouter tous les primaires
            foreach (var pCue in primary)
            {
                var overlapping = secondary.Where(s => 
                    CuesOverlap(pCue, s, toleranceTicks)).ToList();
                
                if (overlapping.Any())
                {
                    var merged = CreateMergedCue(pCue.StartTime, pCue.EndTime, pCue, overlapping.First(), options);
                    if (merged != null)
                        result.Add(merged);
                }
                else
                {
                    // Juste le primaire
                    var cue = CreateMergedCue(pCue.StartTime, pCue.EndTime, pCue, null, options);
                    if (cue != null)
                        result.Add(cue);
                }
            }

            // Ajouter les secondaires non chevauchés
            foreach (var sCue in secondary)
            {
                bool hasOverlap = primary.Any(p => CuesOverlap(p, sCue, toleranceTicks));
                if (!hasOverlap)
                {
                    var cue = CreateMergedCue(sCue.StartTime, sCue.EndTime, null, sCue, options);
                    if (cue != null)
                        result.Add(cue);
                }
            }
            
            // Trier et renuméroter
            result = result.OrderBy(c => c.StartTime).ToList();
            for (int i = 0; i < result.Count; i++)
                result[i].Index = i + 1;
            
            return result;
        }
        
        /// <summary>
        /// Convertit la valeur reçue de l'interface en disposition
        /// </summary>
        public static DualLayout ParseLayout(string? value)
        {
            return string.Equals(value, "stacked", StringComparison.OrdinalIgnoreCase)
                ? DualLayout.Stacked
                : DualLayout.TopBottom;
        }

        /// <summary>
        /// Convertit la valeur reçue de l'interface (all, overlapping, primary) en mode de fusion
        /// </summary>
        public static MergeMode ParseMode(string? value)
        {
            return (value ?? string.Empty).ToLowerInvariant() switch
            {
                "overlapping" => MergeMode.OverlappingOnly,
                "primary" => MergeMode.PrimaryPriority,
                _ => MergeMode.AllCues
            };
        }

        /// <summary>
        /// Texte d'une réplique source, sans ses balises de position.
        /// La position est décidée par la disposition choisie : une balise {\an8} héritée
        /// de la source enverrait la langue du bas en haut.
        /// </summary>
        private static string CleanText(SubtitleCue cue)
        {
            return PositionTagRegex.Replace(cue.Text, string.Empty).Trim();
        }

        private static readonly Regex PositionTagRegex = new Regex(@"\{\\an?\d+\}", RegexOptions.Compiled);

        /// <summary>
        /// Disposition haut/bas : chaque langue garde ses propres répliques et son propre minutage.
        /// En SRT, {\an8} s'applique à une réplique entière : une réplique qui contiendrait les deux
        /// langues monterait en bloc en haut, et le texte sauterait d'une position à l'autre.
        /// </summary>
        private static List<SubtitleCue> MergeTopBottom(
            List<SubtitleCue> primary,
            List<SubtitleCue> secondary,
            MergeOptions options)
        {
            var toleranceTicks = TimeSpan.FromMilliseconds(options.ToleranceMs).Ticks;

            SnapTimings(primary, secondary, toleranceTicks / 2);

            IEnumerable<SubtitleCue> top = primary;
            IEnumerable<SubtitleCue> bottom = secondary;

            if (options.Mode == MergeMode.OverlappingOnly)
            {
                top = primary.Where(p => secondary.Any(s => CuesOverlap(p, s, toleranceTicks)));
                bottom = secondary.Where(s => primary.Any(p => CuesOverlap(p, s, toleranceTicks)));
            }
            else if (options.Mode == MergeMode.PrimaryPriority)
            {
                // Le secondaire accompagne le primaire (première réplique qui chevauche) ou comble les trous
                var paired = new HashSet<SubtitleCue>();
                foreach (var pCue in primary)
                {
                    var match = secondary.FirstOrDefault(s => CuesOverlap(pCue, s, toleranceTicks));
                    if (match != null) paired.Add(match);
                }

                bottom = secondary.Where(s =>
                    paired.Contains(s) || !primary.Any(p => CuesOverlap(p, s, toleranceTicks)));
            }

            IEnumerable<SubtitleCue> Track(IEnumerable<SubtitleCue> cues, string prefix, string? color)
            {
                foreach (var cue in cues)
                {
                    var text = CleanText(cue);
                    if (text.Length == 0) continue;

                    yield return new SubtitleCue
                    {
                        StartTime = cue.StartTime,
                        EndTime = cue.EndTime,
                        Text = prefix + Colorize(text, color)
                    };
                }
            }

            var result = Track(top, TopPositionTag, options.PrimaryColor)
                .Concat(Track(bottom, string.Empty, options.SecondaryColor))
                // Tri stable : à minutage égal, la réplique du haut reste avant celle du bas
                .OrderBy(c => c.StartTime)
                .ToList();

            for (int i = 0; i < result.Count; i++)
                result[i].Index = i + 1;

            return result;
        }

        private static string Colorize(string text, string? color)
        {
            return string.IsNullOrEmpty(color) ? text : $"<font color=\"{color}\">{text}</font>";
        }

        /// <summary>
        /// Crée une réplique empilée : langue 1 puis langue 2, l'une sous l'autre, sans balise de position
        /// </summary>
        private static SubtitleCue? CreateMergedCue(
            TimeSpan start,
            TimeSpan end,
            SubtitleCue? primary,
            SubtitleCue? secondary,
            MergeOptions options)
        {
            var parts = new List<string>(2);

            var primaryText = primary == null ? string.Empty : CleanText(primary);
            if (primaryText.Length > 0)
                parts.Add(Colorize(primaryText, options.PrimaryColor));

            var secondaryText = secondary == null ? string.Empty : CleanText(secondary);
            if (secondaryText.Length > 0)
                parts.Add(Colorize(secondaryText, options.SecondaryColor));

            if (parts.Count == 0)
                return null;

            return new SubtitleCue
            {
                StartTime = start,
                EndTime = end,
                Text = string.Join(Environment.NewLine, parts)
            };
        }
        
        /// <summary>
        /// Vérifie si deux cues se chevauchent
        /// </summary>
        private static bool CuesOverlap(SubtitleCue a, SubtitleCue b, long toleranceTicks)
        {
            var aStart = a.StartTime.Ticks - toleranceTicks;
            var aEnd = a.EndTime.Ticks + toleranceTicks;
            var bStart = b.StartTime.Ticks;
            var bEnd = b.EndTime.Ticks;
            
            return aStart < bEnd && bStart < aEnd;
        }
        
        /// <summary>
        /// Fusionne les cues adjacents avec le même texte
        /// </summary>
        private static List<SubtitleCue> MergeAdjacentCues(List<SubtitleCue> cues, long toleranceTicks)
        {
            if (cues.Count <= 1)
                return cues;
            
            var result = new List<SubtitleCue>();
            var current = cues[0];
            
            for (int i = 1; i < cues.Count; i++)
            {
                var next = cues[i];
                
                // Si même texte et adjacent (avec tolérance)
                if (current.Text == next.Text && 
                    Math.Abs(current.EndTime.Ticks - next.StartTime.Ticks) <= toleranceTicks)
                {
                    // Étendre le cue actuel
                    current.EndTime = next.EndTime;
                }
                else
                {
                    result.Add(current);
                    current = next;
                }
            }
            
            result.Add(current);
            return result;
        }
        
        /// <summary>
        /// Fusionne deux fichiers SRT et écrit le résultat
        /// </summary>
        public static void MergeFiles(
            string primaryPath, 
            string secondaryPath, 
            string outputPath,
            MergeOptions? options = null)
        {
            var primary = SrtParser.ParseFile(primaryPath);
            var secondary = SrtParser.ParseFile(secondaryPath);
            
            var merged = Merge(primary, secondary, options);
            
            SrtParser.WriteFile(outputPath, merged);
        }
    }
}


