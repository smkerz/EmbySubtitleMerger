using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Langue connue du plugin. C'est la seule table de langues : le nom des fichiers,
    /// le prompt de traduction et la détection s'appuient tous dessus.
    /// </summary>
    public class LanguageInfo
    {
        /// <summary>Code ISO 639-1, utilisé dans le nom des fichiers</summary>
        public string Code { get; }

        /// <summary>Nom dans la langue elle-même, affiché dans Emby</summary>
        public string NativeName { get; }

        /// <summary>Nom anglais, utilisé dans le prompt de traduction</summary>
        public string EnglishName { get; }

        /// <summary>Autres codes reconnus (ISO 639-2, variantes)</summary>
        public string[] Aliases { get; }

        public LanguageInfo(string code, string nativeName, string englishName, params string[] aliases)
        {
            Code = code;
            NativeName = nativeName;
            EnglishName = englishName;
            Aliases = aliases;
        }
    }

    /// <summary>
    /// Nommage des sous-titres bilingues.
    /// Emby lit le nom du fichier : "Film.Français + English.fr.srt" s'affiche avec la langue
    /// "fr" et le titre "Français + English", là où "Film.dual.srt" n'affichait que "dual".
    /// </summary>
    public static class SubtitleNaming
    {
        /// <summary>
        /// Ancien suffixe des sous-titres bilingues. Il sert encore de repli quand
        /// les deux langues d'une fusion ne sont pas identifiées.
        /// </summary>
        public const string LegacyDualSuffix = ".dual.srt";

        private static readonly LanguageInfo[] Languages =
        {
            new LanguageInfo("fr", "Français", "French", "fre", "fra", "french"),
            new LanguageInfo("en", "English", "English", "eng", "english"),
            new LanguageInfo("es", "Español", "Spanish", "spa", "spanish"),
            new LanguageInfo("de", "Deutsch", "German", "ger", "deu", "german"),
            new LanguageInfo("it", "Italiano", "Italian", "ita", "italian"),
            new LanguageInfo("pt", "Português", "Portuguese", "por", "pob", "portuguese"),
            new LanguageInfo("nl", "Nederlands", "Dutch", "dut", "nld", "dutch"),
            new LanguageInfo("sv", "Svenska", "Swedish", "swe", "swedish"),
            new LanguageInfo("da", "Dansk", "Danish", "dan", "danish"),
            new LanguageInfo("no", "Norsk", "Norwegian", "nor", "nob", "nb", "nn", "norwegian"),
            new LanguageInfo("fi", "Suomi", "Finnish", "fin", "finnish"),
            new LanguageInfo("pl", "Polski", "Polish", "pol", "polish"),
            new LanguageInfo("cs", "Čeština", "Czech", "cze", "ces", "czech"),
            new LanguageInfo("hu", "Magyar", "Hungarian", "hun", "hungarian"),
            new LanguageInfo("ro", "Română", "Romanian", "rum", "ron", "romanian"),
            new LanguageInfo("tr", "Türkçe", "Turkish", "tur", "turkish"),
            new LanguageInfo("id", "Bahasa Indonesia", "Indonesian", "ind", "indonesian"),
            new LanguageInfo("vi", "Tiếng Việt", "Vietnamese", "vie", "vietnamese"),
            new LanguageInfo("ru", "Русский", "Russian", "rus", "russian"),
            new LanguageInfo("uk", "Українська", "Ukrainian", "ukr", "ukrainian"),
            new LanguageInfo("el", "Ελληνικά", "Greek", "gre", "ell", "greek"),
            new LanguageInfo("he", "עברית", "Hebrew", "heb", "iw", "hebrew"),
            new LanguageInfo("ar", "العربية", "Arabic", "ara", "arabic"),
            new LanguageInfo("hi", "हिन्दी", "Hindi", "hin", "hindi"),
            new LanguageInfo("th", "ไทย", "Thai", "tha", "thai"),
            new LanguageInfo("ko", "한국어", "Korean", "kor", "korean"),
            new LanguageInfo("ja", "日本語", "Japanese", "jpn", "japanese"),
            new LanguageInfo("zh", "中文", "Chinese (Simplified)", "chi", "zho", "chinese")
        };

        private static readonly Dictionary<string, LanguageInfo> ByAlias = BuildAliasIndex();

        private static Dictionary<string, LanguageInfo> BuildAliasIndex()
        {
            var index = new Dictionary<string, LanguageInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var language in Languages)
            {
                index[language.Code] = language;
                index[language.NativeName] = language;
                foreach (var alias in language.Aliases)
                    index[alias] = language;
            }
            return index;
        }

        /// <summary>
        /// Retrouve une langue à partir d'un code Emby (fr, fre, fra, pt-BR…). Null si inconnue.
        /// </summary>
        public static LanguageInfo? Resolve(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            var normalized = code!.Trim();
            if (ByAlias.TryGetValue(normalized, out var language)) return language;

            // "pt-BR", "zh_Hant" : on retombe sur la langue de base
            var separator = normalized.IndexOfAny(new[] { '-', '_' });
            if (separator > 0 && ByAlias.TryGetValue(normalized.Substring(0, separator), out language))
                return language;

            return null;
        }

        /// <summary>
        /// Langue d'un fichier SRT : le code fourni s'il est connu, sinon détection sur le contenu.
        /// </summary>
        public static LanguageInfo? ResolveOrDetect(string? code, string srtPath)
        {
            var language = Resolve(code);
            if (language != null) return language;

            try
            {
                return Resolve(LanguageDetector.DetectSingle(SrtParser.ParseFile(srtPath)));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Nom du sous-titre bilingue. Sans les deux langues (langue non reconnue), il s'appelle Film.dual.srt.
        /// </summary>
        /// <param name="isDefault">Ajoute ".default" : Emby sélectionne alors ce sous-titre de lui-même</param>
        public static string DualFileName(string videoName, LanguageInfo? top, LanguageInfo? bottom, bool isDefault = false)
        {
            if (top == null || bottom == null || top.Code == bottom.Code)
                return videoName + LegacyDualSuffix;

            return $"{videoName}.{top.NativeName} + {bottom.NativeName}.{top.Code}{(isDefault ? ".default" : string.Empty)}.srt";
        }

        /// <summary>
        /// Indique si un nom de fichier est celui d'un bilingue de la vidéo donnée : l'ancien Film.dual.srt,
        /// un "Film.dual.….srt" fait par un autre outil, ou la forme exacte produite par DualFileName
        /// avec deux langues du catalogue.
        /// </summary>
        public static bool IsDualFileName(string videoName, string fileName)
        {
            if (!fileName.StartsWith(videoName + ".", StringComparison.Ordinal)) return false;

            if (fileName.Equals(videoName + LegacyDualSuffix, StringComparison.OrdinalIgnoreCase)) return true;

            // Bilingues faits par d'autres outils : "Film.dual.ru-en.en.srt". Le titre commence par "dual"
            if (fileName.StartsWith(videoName + ".dual.", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // "Français + English.fr.srt" ou "Français + English.fr.default.srt"
            var parts = fileName.Substring(videoName.Length + 1).Split('.');
            if (parts.Length == 4 && parts[2] == "default")
                parts = new[] { parts[0], parts[1], parts[3] };
            if (parts.Length != 3 || !parts[2].Equals("srt", StringComparison.OrdinalIgnoreCase)) return false;

            var names = parts[0].Split(new[] { " + " }, StringSplitOptions.None);
            if (names.Length != 2) return false;

            var top = Resolve(names[0]);
            var bottom = Resolve(names[1]);
            return top != null && bottom != null && top.NativeName == names[0] && bottom.NativeName == names[1] && top.Code == parts[1];
        }

        /// <summary>
        /// Chemin du sous-titre bilingue d'une vidéo, à côté de celle-ci
        /// </summary>
        public static string DualPath(string videoPath, LanguageInfo? top, LanguageInfo? bottom, bool isDefault = false)
        {
            return Path.Combine(
                Path.GetDirectoryName(videoPath) ?? ".",
                DualFileName(Path.GetFileNameWithoutExtension(videoPath), top, bottom, isDefault));
        }

        /// <summary>
        /// Chemin d'un sous-titre monolingue d'une vidéo : "Film.fr.srt"
        /// </summary>
        public static string SinglePath(string videoPath, LanguageInfo language)
        {
            return Path.Combine(
                Path.GetDirectoryName(videoPath) ?? ".",
                $"{Path.GetFileNameWithoutExtension(videoPath)}.{language.Code}.srt");
        }

        /// <summary>
        /// Indique si deux chemins désignent le même fichier
        /// </summary>
        public static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Détection de langue par mots courants et par écriture.
    /// Suffisant pour un fichier de sous-titres entier (des milliers de mots), pas pour une phrase isolée.
    /// </summary>
    public static class LanguageDetector
    {
        private static readonly Regex TagRegex = new Regex(@"\{[^}]*\}|<[^>]*>", RegexOptions.Compiled);
        private static readonly Regex WordRegex = new Regex(@"\p{L}+", RegexOptions.Compiled);

        /// <summary>Score minimal pour retenir une langue</summary>
        private const double MinScore = 15;

        /// <summary>
        /// Part des mots d'un texte latin que la liste de mots courants reconnaît, environ.
        /// Appliquée aux écritures où tous les mots comptent, pour comparer à échelle égale.
        /// </summary>
        private const double ScriptWordWeight = 0.4;

        /// <summary>Caractères par mot compté, pour les écritures sans espaces</summary>
        private const double CharsPerScoredWord = 6.0;

        private static readonly char[] UkrainianLetters = { 'і', 'ї', 'є', 'ґ' };
        private static readonly char[] RussianLetters = { 'ы', 'э', 'ъ', 'ё' };

        private static readonly Dictionary<string, string> CommonWords = new Dictionary<string, string>
        {
            ["fr"] = "le les des une est et je tu il elle nous vous ils pas que qui dans pour avec sur mais ce cette ça mon ton son ma au du être avoir suis êtes sont ai avez ont oui non très bien alors comme quoi où ici tout plus rien jamais toujours peut faire fait merci pourquoi parce quand aussi moi toi lui leur c j n l d qu s m t",
            ["en"] = "the and you is are was were be to of in it that this for with not have has had do does did what who where when why how my your his her they we he she i but just know get got can will would there here yes all about out up right like want going don think now one so if at on from him them our been okay well s t m d ll re ve",
            ["es"] = "el los las una es y yo tú él ella nosotros usted ustedes no que qué en para con por pero este esta esto mi tu su estar estoy está están soy eres somos son tengo tiene hay sí muy bien entonces como cómo dónde aquí todo más nada nunca siempre puede hacer gracias porque cuando también lo le se del al ahora vamos quiero eso ese",
            ["de"] = "der die das und ich du er sie wir ihr nicht ist sind war ein eine einen zu in mit für auf aber was wer wo wie warum wenn mein dein sein haben habe hast hat ja nein sehr gut dann hier alles mehr nichts nie immer kann machen danke weil auch mich dich den dem des von es so noch nur schon doch bin bist uns",
            ["it"] = "il lo gli una è e io tu lui lei noi voi loro non che chi in per con su ma questo questa mio tuo suo essere sono sei siamo ho hai ha abbiamo hanno sì molto bene allora come cosa dove qui tutto più niente mai sempre può fare grazie perché quando anche mi ti ci del della di da al adesso ora voglio c l un",
            ["pt"] = "o os as um uma é e eu tu você ele ela nós vocês eles não que quem em para com por mas este esta isto isso meu teu seu minha estar estou está estão sou somos são tenho tem sim muito bem então como onde aqui tudo mais nada nunca sempre pode fazer obrigado porque quando também do da dos no na ao agora vamos quero foi",
            ["nl"] = "de het een en ik jij je hij zij wij we jullie niet is zijn was te in met voor op maar wat wie waar hoe waarom als mijn jouw hebben heb hebt heeft ja nee heel goed dan hier alles meer niets nooit altijd kan doen bedankt omdat ook mij jou van dat dit er nog naar ben bent ons geen moet",
            ["sv"] = "och jag du han hon vi ni de inte är var en ett att i med för på men vad vem hur varför om min din har hade ja nej mycket bra då här allt mer inget aldrig alltid kan göra tack också mig dig av det den till från nu så ska vill vet",
            ["da"] = "og jeg du han hun vi ikke er var en et at i med for på men hvad hvem hvor hvordan hvorfor hvis min din har havde ja nej meget godt så her alt mere intet aldrig altid kan gøre tak også mig dig af det den til fra nu skal vil ved",
            ["no"] = "og jeg du han hun vi ikke er var en et å i med for på men hva hvem hvor hvordan hvorfor hvis min din har hadde ja nei veldig bra så her alt mer ingenting aldri alltid kan gjøre takk også meg deg av det den til fra nå skal vil vet",
            ["fi"] = "ja minä sinä hän me te he ei on olen olet ovat että mutta mitä kuka missä miten miksi jos minun sinun kyllä hyvin hyvä täällä kaikki mitään koskaan aina voi kiitos koska myös minua sinua se tämä nyt vain niin oli en et mä sä mun sun",
            ["pl"] = "i w nie to się na że jest co jak ja ty on ona my wy oni z do dla ale tak bardzo dobrze tu tutaj wszystko nic nigdy zawsze może dziękuję bo kiedy też mnie ci go jej ten ta są był mam masz ma czy gdzie dlaczego po za już tylko teraz proszę",
            ["cs"] = "a já ty on ona my vy oni ne je jsem jsi jsou v s pro na ale co kdo kde jak proč když můj mám máš má ano velmi dobře tady všechno nic nikdy vždy může děkuju protože taky mě tě se to že z do teď chci tak jen už být byl",
            ["hu"] = "és én te ő mi ti ők nem van vagyok vagy egy a az hogy de mit ki hol hogyan miért ha igen nagyon jó itt minden semmi soha mindig lehet köszönöm mert is engem téged ez azt meg csak most már kell akarok volt még",
            ["ro"] = "și eu tu el ea noi voi ei nu este e sunt un o în cu pentru pe dar ce cine unde cum dacă meu tău am ai are da foarte bine atunci aici tot mai nimic niciodată mereu poate face mulțumesc când mă te se la din acum vreau asta că să",
            ["tr"] = "ve bir bu ben sen o biz siz onlar değil için ile ama ne kim nerede nasıl neden evet hayır çok iyi burada her şey hiç daha var yok teşekkürler çünkü de da mi mı beni seni şimdi tamam gibi kadar sonra bana sana olarak oldu lütfen",
            ["id"] = "dan yang di ini itu aku kamu dia kami kita mereka tidak bukan adalah untuk dengan tapi apa siapa mana bagaimana kenapa kalau ya sangat baik sini semua lagi tak pernah selalu bisa terima kasih karena juga saya anda ke dari sekarang akan sudah ada mau tahu",
            ["vi"] = "không của và là có tôi anh em cái này được một những người cho với rồi đi ở gì sao thì mà nó chúng ta đã sẽ rất cũng"
        };

        /// <summary>Mot → langue, limité aux mots propres à une seule langue de la liste</summary>
        private static readonly Dictionary<string, string> ExclusiveWords = BuildExclusiveWords();

        private static Dictionary<string, string> BuildExclusiveWords()
        {
            var owners = new Dictionary<string, List<string>>();
            foreach (var entry in CommonWords)
            {
                foreach (var word in entry.Value.Split(' ').Distinct())
                {
                    if (!owners.TryGetValue(word, out var list))
                        owners[word] = list = new List<string>();
                    list.Add(entry.Key);
                }
            }

            // Un mot partagé ("de", "la", "en"…) ne départage rien : on ne garde que les mots exclusifs
            return owners.Where(o => o.Value.Count == 1).ToDictionary(o => o.Key, o => o.Value[0]);
        }

        /// <summary>
        /// Score de chaque langue pour un texte, en « mots courants reconnus ».
        /// Pour les langues latines, seuls les mots de la liste comptent ; pour les autres écritures,
        /// tous les mots comptent, d'où la pondération qui garde les deux échelles comparables.
        /// </summary>
        private static Dictionary<string, double> Score(string text)
        {
            var scores = new Dictionary<string, double>();
            void Add(string language, double amount)
            {
                if (amount > 0) scores[language] = scores.TryGetValue(language, out var s) ? s + amount : amount;
            }

            var clean = TagRegex.Replace(text, " ").ToLowerInvariant();

            int cyrillicWords = 0, russianWords = 0, ukrainianWords = 0;
            int greek = 0, hebrew = 0, arabic = 0, devanagari = 0, hangul = 0;

            foreach (Match match in WordRegex.Matches(clean))
            {
                var word = match.Value;

                if (ExclusiveWords.TryGetValue(word, out var language))
                {
                    Add(language, 1);
                    continue;
                }

                var first = word[0];
                if (first >= 0x0400 && first <= 0x04FF)
                {
                    cyrillicWords++;
                    if (word.IndexOfAny(UkrainianLetters) >= 0) ukrainianWords++;
                    else if (word.IndexOfAny(RussianLetters) >= 0) russianWords++;
                }
                else if (first >= 0x0370 && first <= 0x03FF) greek++;
                else if (first >= 0x0590 && first <= 0x05FF) hebrew++;
                else if (first >= 0x0600 && first <= 0x06FF) arabic++;
                else if (first >= 0x0900 && first <= 0x097F) devanagari++;
                else if (first >= 0xAC00 && first <= 0xD7AF) hangul++;
            }

            // Russe et ukrainien partagent l'alphabet : seules quelques lettres les distinguent.
            // Les mots sans lettre distinctive sont répartis comme ceux qui en ont.
            if (ukrainianWords == 0)
            {
                Add("ru", cyrillicWords * ScriptWordWeight);
            }
            else if (russianWords == 0)
            {
                Add("uk", cyrillicWords * ScriptWordWeight);
            }
            else
            {
                var ukrainianShare = (double)ukrainianWords / (ukrainianWords + russianWords);
                Add("uk", cyrillicWords * ukrainianShare * ScriptWordWeight);
                Add("ru", cyrillicWords * (1 - ukrainianShare) * ScriptWordWeight);
            }

            Add("el", greek * ScriptWordWeight);
            Add("he", hebrew * ScriptWordWeight);
            Add("ar", arabic * ScriptWordWeight);
            Add("hi", devanagari * ScriptWordWeight);
            Add("ko", hangul * ScriptWordWeight);

            // Écritures sans espaces entre les mots : on compte les caractères
            int thai = 0, kana = 0, han = 0;
            foreach (var ch in clean)
            {
                if (ch >= 0x0E00 && ch <= 0x0E7F) thai++;
                else if (ch >= 0x3040 && ch <= 0x30FF) kana++;
                else if (ch >= 0x4E00 && ch <= 0x9FFF) han++;
            }

            Add("th", thai / CharsPerScoredWord);

            // Le japonais mélange kana et kanji ; du han sans kana est du chinois
            if (kana * 20 >= kana + han && kana > 0) Add("ja", (kana + han) / CharsPerScoredWord);
            else Add("zh", han / CharsPerScoredWord);

            return scores;
        }

        /// <summary>
        /// Langue dominante d'un sous-titre monolingue. Null si indéterminée.
        /// </summary>
        public static string? DetectSingle(IEnumerable<SubtitleCue> cues)
        {
            var scores = Score(JoinText(cues));
            if (scores.Count == 0) return null;

            var best = scores.OrderByDescending(s => s.Value).First();
            return best.Value >= MinScore ? best.Key : null;
        }

        private static string JoinText(IEnumerable<SubtitleCue> cues)
        {
            return string.Join("\n", cues.Select(c => c.Text));
        }
    }
}
