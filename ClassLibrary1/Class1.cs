using System;
using System.Collections.Generic;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net.Http;
using System.Text;
using System.Net;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace EmbySubtitleMerger
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public override string Name => "Subtitle Merger Plugin";
        
        public override Guid Id => new Guid("12345678-1234-1234-1234-123456789012");
        
        public override string Description => "Plugin pour fusionner des sous-titres de deux langues différentes";


        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) 
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            try
            {
                Log("=== Plugin initialized ===");
                Log($"Assembly: {Assembly.GetExecutingAssembly().FullName}");
                var resources = Assembly.GetExecutingAssembly().GetManifestResourceNames();
                Log("Embedded resources:");
                foreach (var r in resources)
                {
                    Log(" - " + r);
                }
                // Probe the expected html
                var expected = "EmbySubtitleMerger.Configuration.configPage.html";
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(expected);
                Log(s != null ? $"Found expected resource: {expected}" : $"Missing expected resource: {expected}");
                
            }
            catch { }
        }

        public static Plugin? Instance { get; private set; }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            try
            {
                Log("GetPages called - Returning configuration page + JS + injector");
                return new[]
                {
                    new PluginPageInfo
                    {
                        Name = "subtitlemerger",
                        EmbeddedResourcePath = "EmbySubtitleMerger.Configuration.configPage.html",
                        EnableInMainMenu = true,
                        MenuSection = "server",
                        MenuIcon = "closed_caption",
                        DisplayName = "Subtitle Merger"
                    },
                    new PluginPageInfo
                    {
                        Name = "subtitlemerger.js",
                        EmbeddedResourcePath = "EmbySubtitleMerger.Configuration.configPage.js"
                    },
                    new PluginPageInfo
                    {
                        Name = "subtitlepageinjector.js",
                        EmbeddedResourcePath = "EmbySubtitleMerger.Configuration.subtitlePageInjector.js"
                    }
                };
            }
            catch (Exception ex)
            {
                Log("GetPages ERROR: " + ex.Message);
                return Array.Empty<PluginPageInfo>();
            }
        }
        
        private string GenerateSubtitleMergerPage()
        {
            try
            {
                var apiKey = "a482e26698584ef8a83d6ff49c0d8676";
                var userId = "e69b7a5d73a943b4918712599f79905e";
                var apiUrl = $"http://localhost:8096/emby/Users/{userId}/Items?IncludeItemTypes=Movie&Recursive=true&SortBy=SortName&Fields=Path,MediaStreams&api_key={apiKey}";

                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = httpClient.GetStringAsync(apiUrl).Result;
                var jsonDoc = JsonDocument.Parse(response);
                var items = jsonDoc.RootElement.GetProperty("Items");

                var options = new StringBuilder();
                options.AppendLine("<option value=\"\">-- Sélectionner un film --</option>");
                
                foreach (var movie in items.EnumerateArray())
                {
                    var id = movie.TryGetProperty("Id", out var idEl) ? idEl.GetString() : "";
                    var name = movie.TryGetProperty("Name", out var nameEl) ? nameEl.GetString() : "?";
                    var year = movie.TryGetProperty("ProductionYear", out var yearEl) ? $" ({yearEl.GetInt32()})" : "";
                    options.AppendLine($"<option value=\"{WebUtility.HtmlEncode(id)}\">{WebUtility.HtmlEncode(name)}{year}</option>");
                }

                var count = 0;
                foreach (var _ in items.EnumerateArray()) count++;

                return $@"
<div data-role=""page"" class=""page type-interior pluginConfigurationPage"" style=""color:#e6eaf0;"">
    <div data-role=""content"">
        <div class=""content-primary"" style=""background:rgba(23,28,34,0.9); padding:20px; border-radius:10px;"">
            <h1 style=""color:#00a4dc;"">Subtitle Merger <span style=""font-size:0.5em; color:#4CAF50;"">v3.0.0 - {count} films</span></h1>
            
            <div class=""verticalSection"">
                <h2 class=""sectionTitle"">1. Sélectionner un film</h2>
                <select id=""moviesSelect"" is=""emby-select"" style=""max-width:600px;"">
                    {options}
                </select>
                <div id=""movieInfo"" style=""margin-top:10px; color:#888;"">Sélectionnez un film pour voir ses sous-titres</div>
            </div>
            
            <div class=""verticalSection"" style=""margin-top:20px;"">
                <h2 class=""sectionTitle"">2. Sous-titres (chargement via iframe)</h2>
                <iframe id=""subtitlesFrame"" name=""subtitlesFrame"" style=""width:100%; height:200px; border:1px solid #2b3743; border-radius:8px; background:#0f1419;"" src=""about:blank""></iframe>
            </div>
            
            <div class=""verticalSection"" style=""margin-top:20px;"">
                <h2 class=""sectionTitle"">Actions</h2>
                <p style=""color:#888;"">Sélectionnez un film ci-dessus, puis cliquez sur ""Charger sous-titres"" pour voir les options.</p>
                <form method=""GET"" action=""/EmbySubtitleMerger/SubtitlesOptions.html"" target=""subtitlesFrame"">
                    <input type=""hidden"" name=""api_key"" value=""{apiKey}"" />
                    <input type=""hidden"" name=""ItemId"" id=""selectedItemId"" value="""" />
                    <button type=""submit"" is=""emby-button"" class=""raised button-submit"">
                        <span>Charger les sous-titres</span>
                    </button>
                </form>
            </div>
            
            <div style=""margin-top:20px; padding:10px; background:rgba(0,0,0,0.3); border-radius:5px; font-size:0.85em; color:#666;"">
                Généré le {DateTime.Now:dd/MM/yyyy HH:mm:ss} | {count} films dans la base
            </div>
        </div>
    </div>
</div>";
            }
            catch (Exception ex)
            {
                Log($"GenerateSubtitleMergerPage ERROR: {ex.Message}");
                return $@"
<div data-role=""page"" class=""page type-interior pluginConfigurationPage"">
    <div data-role=""content"">
        <div class=""content-primary"">
            <h1 style=""color:#ff6b6b;"">Erreur</h1>
            <p>{WebUtility.HtmlEncode(ex.Message)}</p>
        </div>
    </div>
</div>";
            }
        }

        public string GenerateHtmlWithMovies()
        {
            try
            {
                // Récupérer les films depuis l'API
                var apiKey = "a482e26698584ef8a83d6ff49c0d8676";
                var userId = "e69b7a5d73a943b4918712599f79905e";
                var apiUrl = $"http://localhost:8096/emby/Users/{userId}/Items?IncludeItemTypes=Movie&Recursive=true&SortOrder=Ascending&SortBy=SortName&Fields=Overview,ProductionYear,Path&api_key={apiKey}";

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                var response = httpClient.GetStringAsync(apiUrl).Result;
                var jsonDoc = JsonDocument.Parse(response);
                var items = jsonDoc.RootElement.GetProperty("Items");
                var totalCount = jsonDoc.RootElement.GetProperty("TotalRecordCount").GetInt32();

                var moviesHtml = new StringBuilder();
                int index = 0;
                foreach (var movie in items.EnumerateArray())
                {
                    var name = movie.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() : "Titre inconnu";
                    var year = movie.TryGetProperty("ProductionYear", out var yearElement) ? yearElement.GetInt32().ToString() : "";
                    var overview = movie.TryGetProperty("Overview", out var overviewElement) ? overviewElement.GetString() : "Pas de description";
                    var path = movie.TryGetProperty("Path", out var pathElement) ? Path.GetFileName(pathElement.GetString()) : "Fichier inconnu";

                    if (overview != null && overview.Length > 150)
                        overview = overview.Substring(0, 150) + "...";

                    var bgColor = index % 2 == 0 ? "#333" : "#404040";
                    
                    moviesHtml.AppendLine($@"
                        <div style=""margin: 1em 0; padding: 1em; background: {bgColor}; border-radius: 6px; border-left: 4px solid #4CAF50;"">
                            <div style=""color: #fff; font-weight: bold; margin-bottom: 0.5em;"">🎬 {name}{(string.IsNullOrEmpty(year) ? "" : $" ({year})")}</div>
                            <div style=""color: #ccc; font-size: 0.9em; margin-bottom: 0.5em;"">
                                ""{overview}""
                            </div>
                            <div style=""color: #888; font-size: 0.8em; font-family: monospace;"">📁 {path}</div>
                        </div>");
                    
                    index++;
                }

                // Générer le HTML complet
                return $@"
<div data-role=""page"" class=""page type-interior pluginConfigurationPage"" data-require=""emby-input,emby-button"">
    <div data-role=""content"">
        <div class=""content-primary"">
            <h1>🎬 Plugin Films Emby - Version 6.0.0 - HTML SERVEUR GÉNÉRÉ</h1>
            
            <div class=""verticalSection"">
                <h2 class=""sectionTitle"">✅ Plugin fonctionnel avec API serveur !</h2>
                <div class=""fieldDescription"">
                    <div style=""background: #4CAF50; color: white; padding: 1.5em; border-radius: 8px; margin: 1em 0;"">
                        <h3 style=""margin: 0 0 1em 0;"">🎉 HTML généré côté serveur C# !</h3>
                        <p style=""margin: 0;"">Votre plugin génère maintenant le HTML <strong>directement côté serveur</strong> avec vos films.</p>
                    </div>
                </div>
            </div>
            
            <div class=""verticalSection"">
                <h2 class=""sectionTitle"">🎬 Vos films Emby (générés automatiquement)</h2>
                <div class=""fieldDescription"">
                    <div style=""background: #2d5016; padding: 1em; border-radius: 4px; margin: 1em 0;"">
                        <h5 style=""color: #90EE90; margin: 0 0 0.5em 0;"">✅ {totalCount} films générés automatiquement côté serveur :</h5>
                        {moviesHtml}
                        <p style=""color: #4CAF50; margin: 1em 0 0 0; font-size: 0.9em;"">
                            ✅ Total: {totalCount} films | Générés côté serveur C# | {DateTime.Now:dd/MM/yyyy HH:mm:ss}
                        </p>
                    </div>
                </div>
            </div>
            
            <div class=""verticalSection"">
                <h2 class=""sectionTitle"">⚙️ Configuration du plugin</h2>
                <div class=""inputContainer"">
                    <label class=""inputLabel inputLabelUnfocused"" for=""txtLanguage1"">Langue principale:</label>
                    <input is=""emby-input"" type=""text"" id=""txtLanguage1"" value=""en"" />
                    <div class=""fieldDescription"">Langue principale pour les sous-titres</div>
                </div>
                <div class=""inputContainer"">
                    <label class=""inputLabel inputLabelUnfocused"" for=""txtLanguage2"">Langue secondaire:</label>
                    <input is=""emby-input"" type=""text"" id=""txtLanguage2"" value=""fr"" />
                    <div class=""fieldDescription"">Langue secondaire pour les sous-titres</div>
                </div>
                <br/>
                <button is=""emby-button"" type=""submit"" class=""raised button-submit emby-button"">
                    <span>💾 Sauvegarder la configuration</span>
                </button>
            </div>
        </div>
    </div>
</div>";
            }
            catch (Exception ex)
            {
                Log($"GenerateHtmlWithMovies ERROR: {ex.Message}");
                return $@"
<div data-role=""page"" class=""page type-interior pluginConfigurationPage"">
    <div data-role=""content"">
        <div class=""content-primary"">
            <h1>🎬 Plugin Films Emby - Version 6.0.0 - ERREUR</h1>
            <div style=""background: #ff6b6b; color: white; padding: 1.5em; border-radius: 8px; margin: 1em 0;"">
                <h3 style=""margin: 0 0 1em 0;"">❌ Erreur lors de la génération</h3>
                <p style=""margin: 0;"">Impossible de récupérer les films depuis l'API Emby.</p>
                <div style=""color: #fff; font-size: 0.9em; font-family: monospace; margin: 1em 0;"">
                    Erreur: {ex.Message}
                </div>
            </div>
        </div>
    </div>
</div>";
            }
        }


        private static string LogFilePath => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "EmbySubtitleMerger.log");
        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogFilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + message + Environment.NewLine);
            }
            catch { }
        }

        private static void SetOptionalProperty(object obj, string propertyName, object value)
        {
            try
            {
                var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (prop != null && prop.CanWrite)
                {
                    // Convert value if needed (e.g., bool to proper type)
                    object toAssign = value;
                    if (value != null && prop.PropertyType != value.GetType())
                    {
                        toAssign = Convert.ChangeType(value, prop.PropertyType);
                    }
                    prop.SetValue(obj, toAssign);
                    Log($"Set {propertyName} on PluginPageInfo = {toAssign}");
                }
                else
                {
                    Log($"Property {propertyName} not present on PluginPageInfo");
                }
            }
            catch (Exception ex)
            {
                Log($"SetOptionalProperty {propertyName} failed: {ex.Message}");
            }
        }
    }

    // Note: certaines versions ne nécessitent pas IPluginConfigurationPage, on se base sur IHasWebPages.


    [Route("/EmbySubtitleMerger/GetMovies", "GET")]
    public class GetMoviesRequest : IReturn<string>
    {
    }

    public class MoviesApiService : IService
    {
        public async Task<object> Get(GetMoviesRequest request)
        {
            try
            {
                var apiKey = "a482e26698584ef8a83d6ff49c0d8676";
                var userId = "e69b7a5d73a943b4918712599f79905e";
                var apiUrl = $"http://localhost:8096/emby/Users/{userId}/Items?IncludeItemTypes=Movie&Recursive=true&SortOrder=Ascending&SortBy=SortName&Fields=Overview,ProductionYear,Path&api_key={apiKey}";

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                var response = await httpClient.GetStringAsync(apiUrl);
                var jsonDoc = JsonDocument.Parse(response);
                var items = jsonDoc.RootElement.GetProperty("Items");
                var totalCount = jsonDoc.RootElement.GetProperty("TotalRecordCount").GetInt32();

                var html = new StringBuilder();
                html.AppendLine($"<h5 style=\"color: #90EE90; margin: 0 0 0.5em 0;\">✅ {totalCount} films chargés dynamiquement via API serveur :</h5>");

                int index = 0;
                foreach (var movie in items.EnumerateArray())
                {
                    var name = movie.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() : "Titre inconnu";
                    var year = movie.TryGetProperty("ProductionYear", out var yearElement) ? yearElement.GetInt32().ToString() : "";
                    var overview = movie.TryGetProperty("Overview", out var overviewElement) ? overviewElement.GetString() : "Pas de description";
                    var path = movie.TryGetProperty("Path", out var pathElement) ? Path.GetFileName(pathElement.GetString()) : "Fichier inconnu";

                    if (overview != null && overview.Length > 150)
                        overview = overview.Substring(0, 150) + "...";

                    var bgColor = index % 2 == 0 ? "#333" : "#404040";
                    
                    html.AppendLine($@"
<div style=""margin: 1em 0; padding: 1em; background: {bgColor}; border-radius: 6px; border-left: 4px solid #4CAF50;"">
    <div style=""color: #fff; font-weight: bold; margin-bottom: 0.5em;"">🎬 {name}{(string.IsNullOrEmpty(year) ? "" : $" ({year})")}</div>
    <div style=""color: #ccc; font-size: 0.9em; margin-bottom: 0.5em;"">
        ""{overview}""
    </div>
    <div style=""color: #888; font-size: 0.8em; font-family: monospace;"">📁 {path}</div>
</div>");
                    
                    index++;
                }

                html.AppendLine($@"
<p style=""color: #4CAF50; margin: 1em 0 0 0; font-size: 0.9em;"">
    ✅ Total: {totalCount} films | Serveur API C# | {DateTime.Now:dd/MM/yyyy HH:mm:ss}
</p>");

                return html.ToString();
            }
            catch (Exception ex)
            {
                return $@"
<h5 style=""color: #ff6b6b; margin: 0 0 0.5em 0;"">❌ Erreur serveur API</h5>
<p style=""color: #ccc;"">Impossible de récupérer les films depuis l'API Emby.</p>
<div style=""color: #888; font-size: 0.9em; font-family: monospace;"">
    Erreur: {ex.Message}
</div>";
            }
        }

        [Route("/EmbySubtitleMerger/MoviesDropdown", "GET")]
        [Route("/EmbySubtitleMerger/MoviesDropdown.html", "GET")]
        public class MoviesDropdownRequest : IReturn<string>
        {
            public string? Selected { get; set; }
            public int? Size { get; set; }
            public string? Mode { get; set; }
        }

        public async Task<object> Get(MoviesDropdownRequest request)
        {
            try
            {
                var apiKey = "a482e26698584ef8a83d6ff49c0d8676";
                var userId = "e69b7a5d73a943b4918712599f79905e";
                var apiUrl = $"http://localhost:8096/emby/Users/{userId}/Items?IncludeItemTypes=Movie&Recursive=true&SortOrder=Ascending&SortBy=SortName&Fields=Overview,ProductionYear,Path&api_key={apiKey}";

                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await httpClient.GetStringAsync(apiUrl);
                using var jsonDoc = JsonDocument.Parse(response);
                var items = jsonDoc.RootElement.GetProperty("Items");

                var selectedId = request?.Selected ?? string.Empty;
                var mode = request?.Mode?.ToLowerInvariant();
                var size = (mode == "list") ? (request?.Size ?? 10) : 1;

                var html = new StringBuilder();
                html.Append("<html><head><meta charset=\"utf-8\"><style>body{font-family:system-ui,Segoe UI,Arial,sans-serif}</style></head><body style=\"margin:0; background:transparent; padding:8px;\">");
                html.Append("<div style=\"margin-bottom:8px;color:#9fb0c2;\">Mode: <a href=\"/EmbySubtitleMerger/MoviesDropdown.html?api_key=a482e26698584ef8a83d6ff49c0d8676&Mode=combo\" style=\"color:#7fc4ff\">Combo</a> | <a href=\"/EmbySubtitleMerger/MoviesDropdown.html?api_key=a482e26698584ef8a83d6ff49c0d8676&Mode=list&Size=10\" style=\"color:#7fc4ff\">Liste</a></div>");
                html.Append("<form method=\"GET\" action=\"/EmbySubtitleMerger/MoviesDropdown.html?api_key=a482e26698584ef8a83d6ff49c0d8676\" style=\"margin:0\">");
                html.Append($"<input type=\"hidden\" name=\"Mode\" value=\"{(mode == "list" ? "list" : "combo")}\">");
                html.Append($"<select name=\"Selected\" id=\"moviesSelect\" size=\"{size}\" style=\"width:100%;max-width:600px;\">");
                html.Append("<option value=\"\">— Sélectionner —</option>");

                foreach (var movie in items.EnumerateArray())
                {
                    var name = movie.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? "Titre inconnu" : "Titre inconnu";
                    var year = movie.TryGetProperty("ProductionYear", out var yearElement) ? yearElement.GetInt32().ToString() : string.Empty;
                    var id = movie.TryGetProperty("Id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;

                    var label = string.IsNullOrEmpty(year) ? name : $"{name} ({year})";
                    var labelEncoded = WebUtility.HtmlEncode(label);
                    var valueEncoded = WebUtility.HtmlEncode(id);
                    var isSelected = !string.IsNullOrEmpty(selectedId) && string.Equals(id, selectedId, StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty;
                    html.Append($"<option value=\"{valueEncoded}\"{isSelected}>{labelEncoded}</option>");
                }

                html.Append("</select>");
                html.Append("<div style=\"margin-top:8px; display:flex; gap:8px; align-items:center;\">");
                html.Append("<button type=\"submit\" class=\"emby-button raised button-submit\" style=\"padding:8px 12px;\"><span>Choisir</span></button>");
                html.Append("<a href=\"http://localhost:8096/emby/Users/e69b7a5d73a943b4918712599f79905e/Items?IncludeItemTypes=Movie&Recursive=true&SortOrder=Ascending&SortBy=SortName&Fields=Overview,ProductionYear,Path&api_key=a482e26698584ef8a83d6ff49c0d8676\" target=\"_blank\" style=\"color:#7fc4ff;text-decoration:underline;\">Voir le JSON</a>");
                html.Append($"<button type=\"submit\" formaction=\"/EmbySubtitleMerger/SubtitlesOptions.html?api_key={apiKey}\" formtarget=\"subtitlesFrame\" class=\"emby-button raised\" style=\"padding:8px 12px;\"><span>Charger sous-titres</span></button>");
                html.Append("</div>");

                if (!string.IsNullOrEmpty(selectedId))
                {
                    // Find selected label for display
                    string? selectedLabel = null;
                    foreach (var movie in items.EnumerateArray())
                    {
                        var id = movie.TryGetProperty("Id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
                        if (string.Equals(id, selectedId, StringComparison.OrdinalIgnoreCase))
                        {
                            var name = movie.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? "Titre inconnu" : "Titre inconnu";
                            var year = movie.TryGetProperty("ProductionYear", out var yearElement) ? yearElement.GetInt32().ToString() : string.Empty;
                            selectedLabel = string.IsNullOrEmpty(year) ? name : $"{name} ({year})";
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(selectedLabel))
                    {
                        html.Append($"<div style=\"margin-top:8px;color:#9fb0c2;\">Sélectionné: {WebUtility.HtmlEncode(selectedLabel)}</div>");
                    }
                }

                html.Append("</form></body></html>");
                return html.ToString();
            }
            catch (Exception ex)
            {
                var message = WebUtility.HtmlEncode(ex.Message);
                return $"<html><body style=\"margin:0;color:#ff6b6b;font-family:system-ui\">Erreur: {message}</body></html>";
            }
        }
    }

    [Route("/EmbySubtitleMerger/SubtitlesOptions.html", "GET")]
    public class SubtitlesOptionsRequest : IReturn<string>
    {
        public string? ItemId { get; set; }
        public string? Selected { get; set; }
    }

    public class SubtitlesOptionsService : IService
    {
        public async Task<object> Get(SubtitlesOptionsRequest request)
        {
            try
            {
                var rawId = request?.ItemId;
                if (string.IsNullOrWhiteSpace(rawId))
                {
                    rawId = request?.Selected;
                }
                if (string.IsNullOrWhiteSpace(rawId))
                {
                    return "<html><head><meta charset=\"utf-8\"></head><body style=\"margin:0;background:transparent;color:#9fb0c2;font-family:system-ui,Segoe UI,Arial,sans-serif\">Sélectionnez un film d'abord.</body></html>";
                }
                var apiKey = "a482e26698584ef8a83d6ff49c0d8676";
                var itemId = WebUtility.UrlEncode(rawId);
                var apiUrl = $"http://localhost:8096/emby/Items/{itemId}?Fields=MediaStreams&api_key={apiKey}";

                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await httpClient.GetStringAsync(apiUrl);
                using var doc = JsonDocument.Parse(response);

                if (!doc.RootElement.TryGetProperty("MediaStreams", out var streams))
                {
                    return "<!-- no streams -->";
                }

                var options = new StringBuilder();
                foreach (var s in streams.EnumerateArray())
                {
                    var type = s.TryGetProperty("Type", out var typeEl) ? typeEl.GetString() : null;
                    if (!string.Equals(type, "Subtitle", StringComparison.OrdinalIgnoreCase)) continue;

                    // Prefer text subtitle streams
                    var isText = s.TryGetProperty("IsTextSubtitleStream", out var itEl) && itEl.GetBoolean();
                    var codec = s.TryGetProperty("Codec", out var cEl) ? cEl.GetString() ?? "" : "";
                    if (!isText)
                    {
                        var lc = codec.ToLowerInvariant();
                        if (!(lc == "srt" || lc == "ass" || lc == "ssa" || lc == "webvtt" || lc == "subrip" || lc == "mov_text")) continue;
                    }

                    var index = s.TryGetProperty("Index", out var idxEl) ? idxEl.GetInt32() : -1;
                    var lang = s.TryGetProperty("Language", out var lEl) ? lEl.GetString() ?? "" : "";
                    var title = s.TryGetProperty("Title", out var tEl) ? tEl.GetString() ?? "" : "";
                    var isExternal = s.TryGetProperty("IsExternal", out var exEl) && exEl.GetBoolean();

                    var label = new StringBuilder();
                    label.Append(isExternal ? "[ext] " : "[int] ");
                    label.Append($"s:{index} ");
                    if (!string.IsNullOrEmpty(codec)) label.Append(codec + " ");
                    if (!string.IsNullOrEmpty(lang)) label.Append($"lang={lang} ");
                    if (!string.IsNullOrEmpty(title)) label.Append($"\"{title}\"");

                    var value = (isExternal ? "ext:" : "int:") + index.ToString();
                    options.Append("<option value=\"")
                           .Append(WebUtility.HtmlEncode(value))
                           .Append("\">")
                           .Append(WebUtility.HtmlEncode(label.ToString().Trim()))
                           .Append("</option>");
                }
                // Return a simple HTML page with 2 selects so it can render inside an <iframe>
                var html = new StringBuilder();
                html.Append("<html><head><meta charset=\"utf-8\"><style>");
                html.Append("body{font-family:system-ui,Segoe UI,Arial,sans-serif;color:#e6eaf0;background:transparent;margin:0;padding:8px}");
                html.Append(".box{background:#0f1419;border:1px solid #2b3743;border-radius:8px;padding:8px;margin:0 0 8px 0}");
                html.Append("label{color:#9fb0c2;display:block;margin-bottom:4px}");
                html.Append("select{width:100%;max-width:600px;background:#0f1419;color:#e6eaf0;border:1px solid #2b3743;padding:8px;border-radius:8px}");
                html.Append("</style></head><body>");
                html.Append("<div class=\"box\"><label for=\"primaryCombo\">Langue principale</label>");
                html.Append("<select id=\"primaryCombo\"><option value=\"\">— Sélectionner —</option>");
                html.Append(options.ToString());
                html.Append("</select></div>");
                html.Append("<div class=\"box\"><label for=\"secondaryCombo\">Langue secondaire</label>");
                html.Append("<select id=\"secondaryCombo\"><option value=\"\">— Sélectionner —</option>");
                html.Append(options.ToString());
                html.Append("</select></div>");
                html.Append("</body></html>");
                return html.ToString();
            }
            catch (Exception ex)
            {
                var msg = WebUtility.HtmlEncode(ex.Message);
                return $"<html><head><meta charset=\"utf-8\"></head><body style=\"margin:0;background:transparent;color:#ff6b6b;font-family:system-ui\">Erreur: {msg}</body></html>";
            }
        }
    }


    public class PluginConfiguration : BasePluginConfiguration
    {
        public string WelcomeMessage { get; set; } = "Subtitle Merger Plugin Active!";
        public string DefaultLanguage1 { get; set; } = "en";
        public string DefaultLanguage2 { get; set; } = "fr";
        public string OutputFormat { get; set; } = "srt";
    }
}