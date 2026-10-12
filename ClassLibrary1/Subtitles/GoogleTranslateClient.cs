using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Traduction gratuite par le service web de Google Traduction, sans clé ni compte.
    ///
    /// Ce service n'est pas une API officielle : Google peut le limiter ou le fermer sans préavis.
    /// Il traduit phrase par phrase, sans le contexte du film : le résultat est correct pour suivre
    /// un film, moins naturel que celui d'un modèle de langage.
    ///
    /// Les répliques d'un lot sont envoyées une par ligne, et le nombre de lignes rendu est vérifié :
    /// c'est ce qui garde chaque traduction sur le bon minutage.
    /// </summary>
    public sealed class GoogleTranslateClient : ITranslationEngine
    {
        private const string ApiUrl = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t";

        /// <summary>Répliques reçues par appel ; elles sont ensuite réparties en requêtes par taille</summary>
        public const int CuesPerCall = 200;

        /// <summary>
        /// Texte envoyé par requête. Le service accepte bien plus (13 000 caractères essayés sans perte),
        /// mais c'est le nombre de requêtes qui déclenche ses refus : on en envoie peu, de taille moyenne.
        /// </summary>
        private const int MaxCharsPerRequest = 5000;

        /// <summary>Pause entre deux demandes, pour ne pas être pris pour un robot abusif</summary>
        private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(400);

        private static readonly TimeSpan[] RetryDelays =
        {
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(40)
        };

        /// <summary>Longueur au-delà de laquelle une réplique est remise sur deux lignes</summary>
        private const int MaxLineLength = 42;

        private static readonly Regex TagRegex = new Regex(@"<[^>]+>|\{\\[^}]*\}", RegexOptions.Compiled);
        private static readonly Regex SpacesRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex SecondSpeakerRegex = new Regex(@"\s+[-–—]\s*(?=\S)", RegexOptions.Compiled);

        private readonly HttpClient _httpClient;
        private readonly string _source;
        private readonly string _target;

        public GoogleTranslateClient(string? sourceLanguage, string targetLanguage)
        {
            _source = GoogleCode(sourceLanguage) ?? "auto";
            _target = GoogleCode(targetLanguage) ?? targetLanguage;

            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        }

        private static string? GoogleCode(string? language)
        {
            var code = SubtitleNaming.Resolve(language)?.Code;
            return code == "zh" ? "zh-CN" : code;
        }

        public async Task<TranslationBatchResult> TranslateBatchAsync(
            IReadOnlyList<TranslationItem> batch,
            IReadOnlyList<TranslationItem> context,
            CancellationToken cancellationToken = default)
        {
            var result = new TranslationBatchResult { Success = true };

            try
            {
                // Une réplique = une ligne. Les répliques vides ne sont pas envoyées : elles gardent leur texte
                var lines = batch
                    .Select(item => (item.Id, Text: Flatten(item.Text)))
                    .Where(line => line.Text.Length > 0)
                    .ToList();

                foreach (var chunk in SplitBySize(lines))
                    await TranslateGroupAsync(chunk, result.Translations, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Traduit un groupe de répliques en une requête. Si le service rend un autre nombre de lignes
        /// (il en a fusionné ou coupé), le groupe est repris par moitiés, jusqu'à la réplique seule :
        /// seule la zone en cause coûte des requêtes supplémentaires.
        /// </summary>
        private async Task TranslateGroupAsync(
            List<(int Id, string Text)> group, Dictionary<int, string> translations, CancellationToken cancellationToken)
        {
            var translated = (await RequestAsync(string.Join("\n", group.Select(l => l.Text)), cancellationToken)).Split('\n');

            if (translated.Length == group.Count || group.Count == 1)
            {
                for (var i = 0; i < group.Count; i++)
                    translations[group[i].Id] = Rewrap(group.Count == 1 ? string.Join(" ", translated) : translated[i]);
                return;
            }

            var half = group.Count / 2;
            await TranslateGroupAsync(group.GetRange(0, half), translations, cancellationToken);
            await TranslateGroupAsync(group.GetRange(half, group.Count - half), translations, cancellationToken);
        }

        private static IEnumerable<List<(int Id, string Text)>> SplitBySize(List<(int Id, string Text)> lines)
        {
            var chunk = new List<(int Id, string Text)>();
            var size = 0;

            foreach (var line in lines)
            {
                if (chunk.Count > 0 && size + line.Text.Length + 1 > MaxCharsPerRequest)
                {
                    yield return chunk;
                    chunk = new List<(int Id, string Text)>();
                    size = 0;
                }

                chunk.Add(line);
                size += line.Text.Length + 1;
            }

            if (chunk.Count > 0) yield return chunk;
        }

        private async Task<string> RequestAsync(string text, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                await Task.Delay(Pause, cancellationToken);

                using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", text) });
                using var response = await _httpClient.PostAsync($"{ApiUrl}&sl={_source}&tl={_target}", content, cancellationToken);

                if (response.IsSuccessStatusCode)
                    return ParseResponse(await response.Content.ReadAsStringAsync(cancellationToken));

                var temporary = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!temporary || attempt >= RetryDelays.Length)
                {
                    throw new InvalidOperationException(response.StatusCode == HttpStatusCode.TooManyRequests
                        ? "Google Traduction refuse temporairement les demandes venant de ce serveur (trop de demandes). Réessayez dans quelques heures, ou utilisez le moteur Claude."
                        : $"Google Traduction a répondu par une erreur (HTTP {(int)response.StatusCode}).");
                }

                await Task.Delay(RetryDelays[attempt], cancellationToken);
            }
        }

        /// <summary>
        /// La réponse est un tableau de segments [traduction, original, …]. Les lignes sont recomposées d'après
        /// les sauts de ligne du texte d'origine, pas d'après ceux de la traduction : si le service déplace
        /// un saut de ligne en traduisant, chaque traduction reste attachée à la réplique dont elle vient.
        /// </summary>
        internal static string ParseResponse(string raw)
        {
            using var document = JsonDocument.Parse(raw);

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 || root[0].ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Réponse de Google Traduction illisible : le service a peut-être changé.");

            var lines = new List<string>();
            var current = new StringBuilder();

            foreach (var segment in root[0].EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0 || segment[0].ValueKind != JsonValueKind.String)
                    continue;

                current.Append((segment[0].GetString() ?? string.Empty).Replace('\n', ' '));

                var original = segment.GetArrayLength() > 1 && segment[1].ValueKind == JsonValueKind.String ? segment[1].GetString() : null;
                if (original != null && original.EndsWith("\n", StringComparison.Ordinal))
                {
                    lines.Add(current.ToString().Trim());
                    current.Clear();
                }
            }

            lines.Add(current.ToString().Trim());
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Met une réplique sur une seule ligne, sans balises : le service les abîme ou les traduit
        /// </summary>
        internal static string Flatten(string text)
        {
            return SpacesRegex.Replace(TagRegex.Replace(text ?? string.Empty, " "), " ").Trim();
        }

        /// <summary>
        /// Remet une réplique traduite sur deux lignes : une par interlocuteur pour un dialogue
        /// ("- Qui est là ? - Moi."), sinon coupée près du milieu quand elle est trop longue.
        /// </summary>
        internal static string Rewrap(string text)
        {
            text = SpacesRegex.Replace(text ?? string.Empty, " ").Trim();

            if (text.Length > 1 && "-–—".IndexOf(text[0]) >= 0)
            {
                var second = SecondSpeakerRegex.Match(text, 1);
                if (second.Success)
                    return text.Substring(0, second.Index) + "\n" + text.Substring(second.Index).TrimStart();
            }

            if (text.Length <= MaxLineLength) return text;

            // L'espace le plus proche du milieu
            var middle = text.Length / 2;
            var before = text.LastIndexOf(' ', middle);
            var after = text.IndexOf(' ', middle);
            var cut = before < 0 ? after : (after < 0 || middle - before <= after - middle ? before : after);

            return cut <= 0 ? text : text.Substring(0, cut) + "\n" + text.Substring(cut + 1);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
