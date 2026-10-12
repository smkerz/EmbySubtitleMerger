using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Réglages de traduction gardés sur le serveur : ils suivent l'utilisateur d'un navigateur à l'autre.
    /// </summary>
    [Route("/EmbySubtitleMerger/Settings", "GET")]
    [Authenticated(Roles = "Admin")]
    public class GetSettingsRequest : IReturn<SettingsResponse>
    {
    }

    [Route("/EmbySubtitleMerger/Settings", "POST")]
    [Authenticated(Roles = "Admin")]
    public class SaveSettingsRequest : IReturn<SettingsResponse>
    {
        /// <summary>Nouvelle clé API Anthropic. Absente = inchangée ; vide = effacée</summary>
        public string? AnthropicApiKey { get; set; }

        /// <summary>"google" ou "claude". Absent = inchangé</summary>
        public string? TranslationEngine { get; set; }

        /// <summary>Code de la langue cible par défaut (ex: fr). Absent = inchangé</summary>
        public string? DefaultTargetLanguage { get; set; }
    }

    public class SettingsResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }

        /// <summary>La clé elle-même n'est jamais renvoyée au navigateur</summary>
        public bool HasAnthropicKey { get; set; }

        public string? TranslationEngine { get; set; }
        public string? DefaultTargetLanguage { get; set; }
    }

    /// <summary>
    /// Moteur, clé et modèle à utiliser pour une traduction : ce que la requête précise, sinon les réglages enregistrés
    /// </summary>
    public sealed class TranslationSettings
    {
        public string Engine { get; private set; } = TranslationEngines.Google;
        public string? ApiKey { get; private set; }
        public string? Model { get; private set; }

        /// <summary>Renseigné quand la traduction ne peut pas être lancée avec ces réglages</summary>
        public string? Error { get; private set; }

        public ITranslationEngine CreateEngine(string? sourceLanguage, string targetLanguage)
        {
            return TranslationEngines.Create(Engine, ApiKey, Model, sourceLanguage, targetLanguage);
        }

        public static TranslationSettings Resolve(string? engine = null, string? apiKey = null, string? model = null)
        {
            var config = Plugin.Instance?.Configuration;
            var settings = new TranslationSettings
            {
                Engine = (FirstNonBlank(engine, config?.TranslationEngine) ?? TranslationEngines.Google).Trim().ToLowerInvariant(),
                ApiKey = FirstNonBlank(apiKey, config?.AnthropicApiKey),
                Model = FirstNonBlank(model, config?.TranslationModel)
            };

            if (!TranslationEngines.IsKnown(settings.Engine))
                settings.Error = $"Moteur de traduction inconnu: {settings.Engine}";
            else if (settings.Engine == TranslationEngines.Claude && settings.ApiKey == null)
                settings.Error = "Le moteur Claude demande une clé API Anthropic : enregistrez-la dans l'onglet Traduire, ou choisissez Google Traduction.";

            return settings;
        }

        internal static string? FirstNonBlank(string? first, string? second)
        {
            if (!string.IsNullOrWhiteSpace(first)) return first;
            return string.IsNullOrWhiteSpace(second) ? null : second;
        }
    }

    public class SettingsService : IService
    {
        public object Get(GetSettingsRequest request)
        {
            return Describe();
        }

        public object Post(SaveSettingsRequest request)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
                return new SettingsResponse { Error = "Plugin non initialisé." };

            var config = plugin.Configuration;

            if (request.AnthropicApiKey != null)
                config.AnthropicApiKey = request.AnthropicApiKey.Trim();

            if (TranslationEngines.IsKnown(request.TranslationEngine))
                config.TranslationEngine = request.TranslationEngine!;

            var target = SubtitleNaming.Resolve(request.DefaultTargetLanguage);
            if (target != null)
                config.DefaultTargetLanguage = target.Code;

            plugin.SaveConfiguration();
            return Describe();
        }

        private static SettingsResponse Describe()
        {
            var config = Plugin.Instance?.Configuration;

            return new SettingsResponse
            {
                Success = config != null,
                HasAnthropicKey = !string.IsNullOrWhiteSpace(config?.AnthropicApiKey),
                TranslationEngine = config?.TranslationEngine ?? TranslationEngines.Google,
                DefaultTargetLanguage = config?.DefaultTargetLanguage
            };
        }
    }
}
