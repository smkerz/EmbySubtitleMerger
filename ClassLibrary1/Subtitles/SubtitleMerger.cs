using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
    /// Options de fusion
    /// </summary>
    public class MergeOptions
    {
        /// <summary>Mode de fusion</summary>
        public MergeMode Mode { get; set; } = MergeMode.AllCues;
        
        /// <summary>Tolérance en millisecondes pour considérer deux cues comme simultanés</summary>
        public int ToleranceMs { get; set; } = 700;
        
        /// <summary>Couleur du sous-titre primaire (haut) - format ASS/HTML</summary>
        public string? PrimaryColor { get; set; }
        
        /// <summary>Couleur du sous-titre secondaire (bas)</summary>
        public string? SecondaryColor { get; set; }
        
        /// <summary>Ajouter le positionnement {\an8} pour le sous-titre du haut</summary>
        public bool UsePositioning { get; set; } = true;
    }
    
    /// <summary>
    /// Fusionne deux fichiers de sous-titres
    /// </summary>
    public static class SubtitleMerger
    {
        /// <summary>
        /// Fusionne deux listes de sous-titres
        /// </summary>
        /// <param name="primary">Sous-titres primaires (affichés en haut)</param>
        /// <param name="secondary">Sous-titres secondaires (affichés en bas)</param>
        /// <param name="options">Options de fusion</param>
        /// <returns>Liste fusionnée</returns>
        public static List<SubtitleCue> Merge(
            List<SubtitleCue> primary, 
            List<SubtitleCue> secondary, 
            MergeOptions? options = null)
        {
            options ??= new MergeOptions();
            
            return options.Mode switch
            {
                MergeMode.AllCues => MergeAllCues(primary, secondary, options),
                MergeMode.OverlappingOnly => MergeOverlappingOnly(primary, secondary, options),
                MergeMode.PrimaryPriority => MergePrimaryPriority(primary, secondary, options),
                _ => MergeAllCues(primary, secondary, options)
            };
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
                    // Juste le primaire avec positionnement
                    var cue = new SubtitleCue
                    {
                        StartTime = pCue.StartTime,
                        EndTime = pCue.EndTime,
                        Text = options.UsePositioning ? $"{{\\an8}}{pCue.Text}" : pCue.Text
                    };
                    result.Add(cue);
                }
            }
            
            // Ajouter les secondaires non chevauchés
            foreach (var sCue in secondary)
            {
                bool hasOverlap = primary.Any(p => CuesOverlap(p, sCue, toleranceTicks));
                if (!hasOverlap)
                {
                    result.Add(new SubtitleCue
                    {
                        StartTime = sCue.StartTime,
                        EndTime = sCue.EndTime,
                        Text = sCue.Text
                    });
                }
            }
            
            // Trier et renuméroter
            result = result.OrderBy(c => c.StartTime).ToList();
            for (int i = 0; i < result.Count; i++)
                result[i].Index = i + 1;
            
            return result;
        }
        
        /// <summary>
        /// Crée un cue fusionné à partir de deux cues
        /// </summary>
        private static SubtitleCue? CreateMergedCue(
            TimeSpan start, 
            TimeSpan end, 
            SubtitleCue? primary, 
            SubtitleCue? secondary,
            MergeOptions options)
        {
            if (primary == null && secondary == null)
                return null;
            
            var sb = new StringBuilder();
            
            // Texte primaire (en haut)
            if (primary != null && !string.IsNullOrWhiteSpace(primary.Text))
            {
                var text = primary.Text.Trim();
                
                // Ajouter positionnement en haut
                if (options.UsePositioning)
                    sb.Append("{\\an8}");
                
                // Ajouter couleur si spécifiée
                if (!string.IsNullOrEmpty(options.PrimaryColor))
                    sb.Append($"<font color=\"{options.PrimaryColor}\">");
                
                sb.Append(text);
                
                if (!string.IsNullOrEmpty(options.PrimaryColor))
                    sb.Append("</font>");
            }
            
            // Séparateur si les deux existent
            if (primary != null && secondary != null && 
                !string.IsNullOrWhiteSpace(primary.Text) && 
                !string.IsNullOrWhiteSpace(secondary.Text))
            {
                sb.AppendLine();
            }
            
            // Texte secondaire (en bas, position par défaut)
            if (secondary != null && !string.IsNullOrWhiteSpace(secondary.Text))
            {
                var text = secondary.Text.Trim();
                
                // Ajouter couleur si spécifiée
                if (!string.IsNullOrEmpty(options.SecondaryColor))
                    sb.Append($"<font color=\"{options.SecondaryColor}\">");
                
                sb.Append(text);
                
                if (!string.IsNullOrEmpty(options.SecondaryColor))
                    sb.Append("</font>");
            }
            
            var finalText = sb.ToString().Trim();
            if (string.IsNullOrEmpty(finalText))
                return null;
            
            return new SubtitleCue
            {
                StartTime = start,
                EndTime = end,
                Text = finalText
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


