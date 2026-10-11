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
