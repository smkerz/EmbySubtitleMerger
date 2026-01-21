using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Représente une entrée de sous-titre (cue)
    /// </summary>
    public class SubtitleCue
    {
        public int Index { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Text { get; set; } = string.Empty;
        
        public override string ToString()
        {
            return $"{Index}\r\n{FormatTime(StartTime)} --> {FormatTime(EndTime)}\r\n{Text}\r\n";
        }
        
        private static string FormatTime(TimeSpan ts)
        {
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}";
        }
    }
    
    /// <summary>
    /// Parser pour les fichiers SRT
    /// </summary>
    public static class SrtParser
    {
        // Regex pour parser le timecode SRT: 00:01:23,456 --> 00:01:25,789
        private static readonly Regex TimeCodeRegex = new Regex(
            @"(\d{1,2}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})[,.](\d{3})",
            RegexOptions.Compiled);
        
        /// <summary>
        /// Parse un fichier SRT et retourne la liste des sous-titres
        /// </summary>
        public static List<SubtitleCue> ParseFile(string filePath)
        {
            var content = File.ReadAllText(filePath, DetectEncoding(filePath));
            return Parse(content);
        }
        
        /// <summary>
        /// Parse le contenu SRT et retourne la liste des sous-titres
        /// </summary>
        public static List<SubtitleCue> Parse(string content)
        {
            var cues = new List<SubtitleCue>();
            
            // Normaliser les fins de ligne
            content = content.Replace("\r\n", "\n").Replace("\r", "\n");
            
            // Supprimer le BOM si présent
            if (content.Length > 0 && content[0] == '\uFEFF')
                content = content.Substring(1);
            
            // Séparer par double saut de ligne (entre les cues)
            var blocks = Regex.Split(content.Trim(), @"\n\s*\n");
            
            foreach (var block in blocks)
            {
                var cue = ParseBlock(block.Trim());
                if (cue != null)
                    cues.Add(cue);
            }
            
            return cues;
        }
        
        /// <summary>
        /// Parse un bloc de sous-titre individuel
        /// </summary>
        private static SubtitleCue? ParseBlock(string block)
        {
            if (string.IsNullOrWhiteSpace(block))
                return null;
            
            var lines = block.Split('\n');
            if (lines.Length < 2)
                return null;
            
            var cue = new SubtitleCue();
            int lineIndex = 0;
            
            // Ligne 1: Index (optionnel, on peut le recalculer)
            if (int.TryParse(lines[0].Trim(), out int index))
            {
                cue.Index = index;
                lineIndex = 1;
            }
            
            // Ligne 2 (ou 1): Timecode
            if (lineIndex >= lines.Length)
                return null;
                
            var timeMatch = TimeCodeRegex.Match(lines[lineIndex]);
            if (!timeMatch.Success)
            {
                // Peut-être que l'index était manquant, essayer la première ligne
                if (lineIndex == 1)
                {
                    timeMatch = TimeCodeRegex.Match(lines[0]);
                    if (timeMatch.Success)
                        lineIndex = 0;
                    else
                        return null;
                }
                else
                    return null;
            }
            
            cue.StartTime = new TimeSpan(0,
                int.Parse(timeMatch.Groups[1].Value),
                int.Parse(timeMatch.Groups[2].Value),
                int.Parse(timeMatch.Groups[3].Value),
                int.Parse(timeMatch.Groups[4].Value));
                
            cue.EndTime = new TimeSpan(0,
                int.Parse(timeMatch.Groups[5].Value),
                int.Parse(timeMatch.Groups[6].Value),
                int.Parse(timeMatch.Groups[7].Value),
                int.Parse(timeMatch.Groups[8].Value));
            
            lineIndex++;
            
            // Lignes suivantes: Texte
            var textLines = new List<string>();
            for (int i = lineIndex; i < lines.Length; i++)
            {
                textLines.Add(lines[i].Trim());
            }
            cue.Text = string.Join("\r\n", textLines);
            
            return cue;
        }
        
        /// <summary>
        /// Détecte l'encodage d'un fichier
        /// </summary>
        public static Encoding DetectEncoding(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            
            // UTF-8 BOM
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8;
            
            // UTF-16 LE BOM
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode;
            
            // UTF-16 BE BOM
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode;
            
            // Essayer de détecter UTF-8 sans BOM
            if (IsValidUtf8(bytes))
                return Encoding.UTF8;
            
            // Par défaut: Windows-1252 (ANSI Western)
            try
            {
                return Encoding.GetEncoding(1252);
            }
            catch
            {
                return Encoding.UTF8;
            }
        }
        
        /// <summary>
        /// Vérifie si les bytes sont du UTF-8 valide
        /// </summary>
        private static bool IsValidUtf8(byte[] bytes)
        {
            int i = 0;
            while (i < bytes.Length)
            {
                if (bytes[i] <= 0x7F)
                {
                    i++;
                }
                else if (bytes[i] >= 0xC2 && bytes[i] <= 0xDF)
                {
                    if (i + 1 >= bytes.Length || bytes[i + 1] < 0x80 || bytes[i + 1] > 0xBF)
                        return false;
                    i += 2;
                }
                else if (bytes[i] >= 0xE0 && bytes[i] <= 0xEF)
                {
                    if (i + 2 >= bytes.Length)
                        return false;
                    if (bytes[i + 1] < 0x80 || bytes[i + 1] > 0xBF)
                        return false;
                    if (bytes[i + 2] < 0x80 || bytes[i + 2] > 0xBF)
                        return false;
                    i += 3;
                }
                else if (bytes[i] >= 0xF0 && bytes[i] <= 0xF4)
                {
                    if (i + 3 >= bytes.Length)
                        return false;
                    if (bytes[i + 1] < 0x80 || bytes[i + 1] > 0xBF)
                        return false;
                    if (bytes[i + 2] < 0x80 || bytes[i + 2] > 0xBF)
                        return false;
                    if (bytes[i + 3] < 0x80 || bytes[i + 3] > 0xBF)
                        return false;
                    i += 4;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
        
        /// <summary>
        /// Écrit une liste de sous-titres dans un fichier SRT
        /// </summary>
        public static void WriteFile(string filePath, List<SubtitleCue> cues, Encoding? encoding = null)
        {
            encoding ??= new UTF8Encoding(true); // UTF-8 avec BOM
            
            var sb = new StringBuilder();
            int index = 1;
            
            foreach (var cue in cues)
            {
                cue.Index = index++;
                sb.Append(cue.ToString());
                sb.AppendLine();
            }
            
            File.WriteAllText(filePath, sb.ToString(), encoding);
        }
    }
}


