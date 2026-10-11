using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Piste de sous-titres d'une vidéo, telle que ffprobe la décrit
    /// </summary>
    public class SubtitleTrack
    {
        /// <summary>Index absolu de la piste dans la vidéo</summary>
        public int Index { get; set; }

        /// <summary>Rang de la piste parmi les sous-titres de la vidéo, à partir de 1</summary>
        public int Number { get; set; }

        public string Codec { get; set; } = string.Empty;
        public string Language { get; set; } = "und";
        public string Title { get; set; } = string.Empty;

        public bool IsStyled => Codec == "ass" || Codec == "ssa";
        public bool IsText => Codec == "subrip" || Codec == "mov_text" || Codec == "webvtt";
        public bool IsImage => Codec == "hdmv_pgs_subtitle" || Codec == "dvd_subtitle" || Codec == "dvb_subtitle";

        /// <summary>Marquée "forced" dans la vidéo : ne contient que quelques répliques</summary>
        public bool ForcedFlag { get; set; }

        public bool IsForced => ForcedFlag || Title.Contains("forced", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Titre repris dans le nom du .srt converti. Une piste forced sans titre reçoit "forced" :
        /// son fichier n'est pas "default", et sans titre il porterait le nom d'un .srt d'origine annoté.
        /// </summary>
        public string TitleForFile => Title.Length == 0 && IsForced ? "forced" : Title;
    }

    /// <summary>
    /// Règles de nommage de la normalisation, reprises du script emby_subs.py.
    ///
    /// Elles s'appuient sur la façon dont Emby lit un nom de sous-titre : le code de langue se place
    /// juste avant ".srt" (ou avant ".default.srt"), et le texte entre le nom de la vidéo et la langue
    /// s'affiche comme note sous le sous-titre, espaces supprimés.
    /// </summary>
    public static class NormalizeNaming
    {
        /// <summary>Note écrite dans le titre des pistes intégrées</summary>
        public const string Note = "Normalisé pour Emby";

        /// <summary>Note dans les noms de fichiers : Emby supprime les espaces, d'où les tirets</summary>
        public static readonly string FileNoteBase = Note.Replace(' ', '-');

        private const int MaxTitleLength = 40;

        private static readonly HashSet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".webm",
            ".ts", ".m2ts", ".mts", ".mpg", ".mpeg", ".wmv", ".flv", ".ogv", ".divx", ".vob", ".3gp"
        };

        /// <summary>"….dual.srt" ou "….dual.ru-en.en.srt" : un bilingue, quel que soit le nom qui précède</summary>
        private static readonly Regex DualNameRegex = new Regex(@"\.dual(\.[^.]+)*\.srt$",RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Indique si un .srt qui ne correspond à aucune vidéo du dossier porte un nom de bilingue.
        /// Emby rattache parfois un sous-titre à une vidéo dont le nom diffère un peu (plusieurs versions
        /// d'un film, par exemple) : le plugin, lui, ne sait pas à quelle vidéo il appartient.
        /// </summary>
        public static bool IsOrphanBilingual(string srtPath) => DualNameRegex.IsMatch(Path.GetFileName(srtPath));

        private static readonly Regex ForbiddenTitleChars = new Regex(@"[\\/:*?""<>|.()]+", RegexOptions.Compiled);
        private static readonly Regex FontTagRegex = new Regex(@"</?font[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AssCodeRegex = new Regex(@"\{\\[^}]*\}", RegexOptions.Compiled);

        /// <summary>Reste du nom d'une copie de piste texte : note, titre facultatif, (sN), langue</summary>
        private static readonly Regex ExtractedCopyRegex = new Regex(
            "^" + Regex.Escape(FileNoteBase) + @"(\([^()]*\))?\(s\d+\)\.[^.]+\.srt$", RegexOptions.Compiled);

        /// <summary>Ancienne note à espaces : "Normalisé pour Emby - titre (2)"</summary>
        private static readonly Regex LegacyNoteRegex =
            new Regex("^" + Regex.Escape(Note) + @"(?: - (.*?))?(?: \((\d+)\))?$", RegexOptions.Compiled);

        public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));

        public static bool IsStyledSubtitleFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".ass", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ssa", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Seul conteneur à pouvoir porter des pistes ASS/SSA, et le seul que mkvpropedit modifie</summary>
        public static bool IsMkv(string path) =>
            Path.GetExtension(path).Equals(".mkv", StringComparison.OrdinalIgnoreCase);

        public static bool IsSrt(string path) =>
            Path.GetExtension(path).Equals(".srt", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Note sans espaces pour un nom de fichier : Normalisé-pour-Emby(full)(2)
        /// </summary>
        public static string FileNote(string? title, int rank = 1)
        {
            var cleaned = ForbiddenTitleChars.Replace(title ?? string.Empty, " ").Trim();
            if (cleaned.Length > MaxTitleLength)
                cleaned = cleaned.Substring(0, MaxTitleLength);
            cleaned = string.Join("-", cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            var note = FileNoteBase;
            if (cleaned.Length > 0) note += $"({cleaned})";
            if (rank > 1) note += $"({rank})";
            return note;
        }

        private static string Sibling(string videoPath, string fileName)
        {
            return Path.Combine(Path.GetDirectoryName(videoPath) ?? ".", fileName);
        }

        /// <summary>
        /// Fichier créé pour une piste ASS/SSA intégrée : Film.Normalisé-pour-Emby(full).fre.default.srt.
        /// isDefault marque le fichier "default" pour qu'Emby le choisisse : la première piste complète
        /// de chaque langue, jamais une piste "forced".
        /// </summary>
        public static string ConvertedPath(string videoPath, string language, int rank, string? title, bool isDefault)
        {
            var stem = Path.GetFileNameWithoutExtension(videoPath);
            var suffix = isDefault ? ".default" : string.Empty;
            return Sibling(videoPath, $"{stem}.{FileNote(title, rank)}.{language}{suffix}.srt");
        }

        /// <summary>
        /// La vidéo du même dossier à laquelle appartient un sous-titre : celle dont le nom
        /// ouvre le nom du sous-titre. Si plusieurs correspondent, le nom le plus long l'emporte.
        /// </summary>
        public static string? FindVideoFor(string subtitlePath, IEnumerable<string> videosInSameFolder)
        {
            var name = Path.GetFileName(subtitlePath);

            return videosInSameFolder
                .Where(v => name.StartsWith(Path.GetFileNameWithoutExtension(v) + ".", StringComparison.Ordinal))
                .OrderByDescending(v => Path.GetFileNameWithoutExtension(v).Length)
                .FirstOrDefault();
        }

        /// <summary>
        /// Film.fre.srt devient Film.Normalisé-pour-Emby.fre.srt.
        /// Avec newExtension, l'extension est remplacée (Film.rus.ass devient ….rus.srt, Film.ass devient ….srt).
        /// Le sous-titre doit appartenir à la vidéo (son nom commence par celui de la vidéo).
        /// </summary>
        public static string PathWithNote(string subtitlePath, string videoPath, string? newExtension = null)
        {
            var stem = Path.GetFileNameWithoutExtension(videoPath);
            var name = Path.GetFileName(subtitlePath);
            if (newExtension != null)
                name = Path.GetFileNameWithoutExtension(name) + newExtension;

            // ".fre.srt", ou ".srt" quand le sous-titre n'a pas de code de langue
            var rest = name.StartsWith(stem + ".", StringComparison.Ordinal) ? name.Substring(stem.Length) : "." + name;

            return Sibling(subtitlePath, $"{stem}.{FileNoteBase}{rest}");
        }

        /// <summary>
        /// Nouveau nom d'un .srt créé avec l'ancienne note à espaces, ou null s'il n'est pas concerné.
        /// "Film.Normalisé pour Emby - titre (2).rus.srt" devient "Film.Normalisé-pour-Emby(titre)(2).rus.srt".
        /// </summary>
        public static string? MigratedPath(string srtPath, string videoPath)
        {
            var prefix = Path.GetFileNameWithoutExtension(videoPath) + ".";
            var name = Path.GetFileName(srtPath);

            if (!name.StartsWith(prefix + Note, StringComparison.Ordinal))
                return null;

            var remainder = name.Substring(prefix.Length);
            var dot = remainder.IndexOf('.');
            if (dot < 0) return null;

            var match = LegacyNoteRegex.Match(remainder.Substring(0, dot));
            if (!match.Success) return null;

            var rank = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
            return Sibling(srtPath, $"{prefix}{FileNote(match.Groups[1].Value, rank)}.{remainder.Substring(dot + 1)}");
        }

        /// <summary>
        /// Indique si un .srt porte déjà la note, sous sa forme actuelle ou ancienne
        /// </summary>
        public static bool HasNote(string srtPath)
        {
            var name = Path.GetFileName(srtPath);
            return name.Contains(Note, StringComparison.Ordinal) || name.Contains(FileNoteBase, StringComparison.Ordinal);
        }

        /// <summary>
        /// Indique si un .srt est un bilingue créé par ce plugin (Film.dual.srt, Film.Français + English.fr.srt).
        /// Ces fichiers ont déjà leur propre titre : ils ne reçoivent pas la note.
        /// </summary>
        public static bool IsBilingual(string srtPath, string videoPath)
        {
            return SubtitleNaming.IsDualFileName(Path.GetFileNameWithoutExtension(videoPath), Path.GetFileName(srtPath));
        }

        /// <summary>
        /// Indique si un .srt est la copie d'une piste texte intégrée, laissée par l'ancienne option d'extraction :
        /// Film.Normalisé-pour-Emby(titre)(s3).eng.srt. Cas particulier de IsFromEmbeddedTrack : la piste
        /// d'origine est lisible partout, la copie peut donc être retirée sans rien perdre.
        /// </summary>
        public static bool IsExtractedCopy(string srtPath, string videoPath)
        {
            var stem = Path.GetFileNameWithoutExtension(videoPath);
            var name = Path.GetFileName(srtPath);
            if (!name.StartsWith(stem + ".", StringComparison.Ordinal)) return false;

            return ExtractedCopyRegex.IsMatch(name.Substring(stem.Length + 1));
        }

        /// <summary>
        /// Indique si un .srt a été tiré d'une piste intégrée à la vidéo, par ce plugin ou par le script
        /// d'origine : sa note est suivie d'une parenthèse (titre de la piste, rang ou numéro).
        /// Un .srt d'origine simplement renommé porte la note seule, suivie d'un point.
        /// </summary>
        public static bool IsFromEmbeddedTrack(string srtPath, string videoPath)
        {
            var prefix = Path.GetFileNameWithoutExtension(videoPath) + "." + FileNoteBase + "(";
            return Path.GetFileName(srtPath).StartsWith(prefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Retire les balises de style que beaucoup d'appareils affichent mal
        /// </summary>
        public static string CleanSrtText(string text)
        {
            text = FontTagRegex.Replace(text, string.Empty);   // polices / couleurs
            return AssCodeRegex.Replace(text, string.Empty);   // codes ASS restants
        }

        /// <summary>
        /// Titre d'une piste intégrée débarrassé de la note, ou null si elle ne la porte pas
        /// </summary>
        public static string? TrackTitleWithoutNote(string? title)
        {
            if (title == null || !title.StartsWith(Note, StringComparison.Ordinal)) return null;
            return title.Substring(Note.Length).TrimStart(' ', '-');
        }

        /// <summary>
        /// Nom d'origine d'un .srt renommé avec la note (Film.Normalisé-pour-Emby.fre.srt redevient Film.fre.srt),
        /// ou null si le fichier n'est pas un .srt d'origine renommé.
        /// </summary>
        public static string? PathWithoutNote(string srtPath, string videoPath)
        {
            var stem = Path.GetFileNameWithoutExtension(videoPath);
            var name = Path.GetFileName(srtPath);

            foreach (var note in new[] { FileNoteBase, Note })
            {
                var prefix = stem + "." + note + ".";
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    return Sibling(srtPath, stem + "." + name.Substring(prefix.Length));
            }

            return null;
        }

        /// <summary>
        /// Lit la sortie JSON de ffprobe (-select_streams s -of json)
        /// </summary>
        public static List<SubtitleTrack> ParseTracks(string ffprobeJson)
        {
            var tracks = new List<SubtitleTrack>();

            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(ffprobeJson) ? "{}" : ffprobeJson);
            if (!document.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
                return tracks;

            var number = 0;
            foreach (var stream in streams.EnumerateArray())
            {
                number++;

                var track = new SubtitleTrack
                {
                    Number = number,
                    Index = stream.TryGetProperty("index", out var index) ? index.GetInt32() : -1,
                    Codec = stream.TryGetProperty("codec_name", out var codec) ? codec.GetString() ?? string.Empty : string.Empty
                };

                if (stream.TryGetProperty("disposition", out var disposition) && disposition.ValueKind == JsonValueKind.Object
                    && disposition.TryGetProperty("forced", out var forced) && forced.ValueKind == JsonValueKind.Number)
                {
                    track.ForcedFlag = forced.GetInt32() == 1;
                }

                if (stream.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
                {
                    foreach (var tag in tags.EnumerateObject())
                    {
                        // Les noms de balises varient en casse selon le conteneur
                        if (tag.Name.Equals("language", StringComparison.OrdinalIgnoreCase))
                            track.Language = tag.Value.GetString() ?? "und";
                        else if (tag.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
                            track.Title = tag.Value.GetString() ?? string.Empty;
                    }
                }

                tracks.Add(track);
            }

            return tracks;
        }
    }
}
