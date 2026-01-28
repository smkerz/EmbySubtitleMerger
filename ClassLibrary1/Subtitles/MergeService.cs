using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Model.Services;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Requête pour fusionner des sous-titres
    /// </summary>
    [Route("/EmbySubtitleMerger/Merge", "POST")]
    public class MergeSubtitlesRequest : IReturn<MergeSubtitlesResponse>
    {
        /// <summary>Chemin du fichier vidéo</summary>
        public string? VideoPath { get; set; }

        /// <summary>Index du premier sous-titre (haut)</summary>
        public int PrimaryIndex { get; set; }

        /// <summary>Index du second sous-titre (bas)</summary>
        public int SecondaryIndex { get; set; }

        /// <summary>Indique si le sous-titre 1 est externe (fichier séparé)</summary>
        public bool Primary1IsExternal { get; set; } = false;

        /// <summary>Chemin du fichier sous-titre 1 si externe</summary>
        public string? Primary1Path { get; set; }

        /// <summary>Indique si le sous-titre 2 est externe (fichier séparé)</summary>
        public bool Primary2IsExternal { get; set; } = false;

        /// <summary>Chemin du fichier sous-titre 2 si externe</summary>
        public string? Primary2Path { get; set; }

        /// <summary>Mode de fusion: AllCues, OverlappingOnly, PrimaryPriority</summary>
        public string? Mode { get; set; }

        /// <summary>Tolérance en ms</summary>
        public int? ToleranceMs { get; set; }

        /// <summary>Utiliser l'API cloud DoubleSub.io au lieu de la fusion locale</summary>
        public bool UseCloudApi { get; set; } = false;

        /// <summary>Couleur du sous-titre primaire (ex: #FFFFFF)</summary>
        public string? Color1 { get; set; }

        /// <summary>Couleur du sous-titre secondaire (ex: #FFFF00)</summary>
        public string? Color2 { get; set; }

        /// <summary>Decalage en ms pour le sous-titre 1</summary>
        public int Offset1Ms { get; set; } = 0;

        /// <summary>Decalage en ms pour le sous-titre 2</summary>
        public int Offset2Ms { get; set; } = 0;

        /// <summary>Cle API DoubleSub.io (requise pour le mode cloud)</summary>
        public string? DoubleSubApiKey { get; set; }
    }
    
    /// <summary>
    /// Réponse de la fusion
    /// </summary>
    public class MergeSubtitlesResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? OutputPath { get; set; }
        public int? CueCount { get; set; }
        public string? Error { get; set; }
    }
    
    /// <summary>
    /// Requête pour vérifier le statut de FFmpeg
    /// </summary>
    [Route("/EmbySubtitleMerger/Status", "GET")]
    public class GetStatusRequest : IReturn<StatusResponse>
    {
    }
    
    /// <summary>
    /// Réponse du statut
    /// </summary>
    public class StatusResponse
    {
        public bool FfmpegAvailable { get; set; }
        public string? FfmpegVersion { get; set; }
        public string? PluginVersion { get; set; }
        public bool CloudApiAvailable { get; set; }
        public string? CloudApiUrl { get; set; }
    }
    
    /// <summary>
    /// Requête pour obtenir les infos d'un film
    /// </summary>
    [Route("/EmbySubtitleMerger/MovieInfo", "GET")]
    public class GetMovieInfoRequest : IReturn<string>
    {
        public string? ItemId { get; set; }
    }

    /// <summary>
    /// Page popup de fusion rapide pour un film
    /// </summary>
    [Route("/EmbySubtitleMerger/QuickMerge", "GET")]
    [Route("/EmbySubtitleMerger/QuickMerge.html", "GET")]
    public class QuickMergePageRequest : IReturn<string>
    {
        public string? ItemId { get; set; }
    }
    
    /// <summary>
    /// Service API pour la fusion de sous-titres
    /// </summary>
    public class MergeService : IService
    {
        /// <summary>
        /// Vérifie le statut du plugin
        /// </summary>
        public async Task<object> Get(GetStatusRequest request)
        {
            var ffmpegAvailable = await FfmpegHelper.IsAvailableAsync();
            var ffmpegVersion = ffmpegAvailable ? await FfmpegHelper.GetVersionAsync() : null;

            // Vérifier l'API cloud
            bool cloudApiAvailable = false;
            try
            {
                using var apiClient = new DoubleSubApiClient();
                cloudApiAvailable = await apiClient.IsAvailableAsync();
            }
            catch { }

            return new StatusResponse
            {
                FfmpegAvailable = ffmpegAvailable,
                FfmpegVersion = ffmpegVersion,
                PluginVersion = "8.7.1",
                CloudApiAvailable = cloudApiAvailable,
                CloudApiUrl = DoubleSubApiClient.DefaultApiUrl
            };
        }
        
        /// <summary>
        /// Obtient les infos d'un film avec ses sous-titres
        /// </summary>
        public async Task<object> Get(GetMovieInfoRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.ItemId))
                {
                    return CreateHtmlResponse("<p style='color:red'>ItemId requis</p>");
                }
                
                var apiKey = "a482e26698584ef8a83d6ff49c0d8676";
                var userId = "e69b7a5d73a943b4918712599f79905e";
                var url = $"http://localhost:8096/emby/Users/{userId}/Items/{request.ItemId}?Fields=MediaStreams,Path&api_key={apiKey}";
                
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var json = await client.GetStringAsync(url);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                var name = root.TryGetProperty("Name", out var n) ? n.GetString() : "?";
                var path = root.TryGetProperty("Path", out var p) ? p.GetString() : "";
                
                var html = new StringBuilder();
                html.Append($"<h3 style='color:#00a4dc;margin:0 0 10px 0;'>{WebUtility.HtmlEncode(name)}</h3>");
                html.Append($"<p style='color:#888;font-size:0.9em;margin:0 0 15px 0;'>{WebUtility.HtmlEncode(Path.GetFileName(path))}</p>");
                
                if (root.TryGetProperty("MediaStreams", out var streams))
                {
                    html.Append("<div style='margin-top:10px;'>");
                    html.Append("<strong style='color:#9fb0c2;'>Sous-titres disponibles:</strong><br><br>");
                    
                    int subCount = 0;
                    foreach (var stream in streams.EnumerateArray())
                    {
                        var type = stream.TryGetProperty("Type", out var t) ? t.GetString() : "";
                        if (type != "Subtitle") continue;
                        
                        subCount++;
                        var index = stream.TryGetProperty("Index", out var idx) ? idx.GetInt32() : -1;
                        var lang = stream.TryGetProperty("Language", out var l) ? l.GetString() : "und";
                        var codec = stream.TryGetProperty("Codec", out var c) ? c.GetString() : "?";
                        var isExt = stream.TryGetProperty("IsExternal", out var e) && e.GetBoolean();
                        var isText = stream.TryGetProperty("IsTextSubtitleStream", out var txt) && txt.GetBoolean();
                        var title = stream.TryGetProperty("Title", out var ti) ? ti.GetString() : "";
                        
                        var color = isText ? "#4CAF50" : "#ff9800";
                        var typeLabel = isText ? "TXT" : "IMG";
                        var extLabel = isExt ? "[EXT]" : "[INT]";
                        
                        html.Append($"<div style='padding:8px;margin:5px 0;background:rgba(0,0,0,0.3);border-radius:5px;border-left:3px solid {color};'>");
                        html.Append($"<input type='checkbox' class='sub-check' data-index='{index}' data-text='{isText}' style='margin-right:8px;'>");
                        html.Append($"<span style='color:{color};font-weight:bold;'>{typeLabel}</span> ");
                        html.Append($"<span style='color:#888;'>{extLabel}</span> ");
                        html.Append($"<span style='color:#e6eaf0;'>#{index} {lang?.ToUpper()} ({codec})</span>");
                        if (!string.IsNullOrEmpty(title))
                            html.Append($" <span style='color:#666;'>- {WebUtility.HtmlEncode(title)}</span>");
                        if (!isText)
                            html.Append($" <span style='color:#ff9800;font-size:0.8em;'>(nécessite OCR)</span>");
                        html.Append("</div>");
                    }
                    
                    if (subCount == 0)
                    {
                        html.Append("<p style='color:#ff6b6b;'>Aucun sous-titre trouvé dans ce fichier.</p>");
                    }
                    else
                    {
                        html.Append($"<p style='color:#888;margin-top:15px;font-size:0.9em;'>{subCount} sous-titre(s) trouvé(s). Cochez 2 sous-titres texte (TXT) pour la fusion.</p>");
                    }
                    
                    html.Append("</div>");
                }
                
                return CreateHtmlResponse(html.ToString());
            }
            catch (Exception ex)
            {
                return CreateHtmlResponse($"<p style='color:red'>Erreur: {WebUtility.HtmlEncode(ex.Message)}</p>");
            }
        }
        
        /// <summary>
        /// Fusionne deux sous-titres
        /// </summary>
        public async Task<object> Post(MergeSubtitlesRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.VideoPath))
                {
                    return new MergeSubtitlesResponse
                    {
                        Success = false,
                        Error = "VideoPath est requis"
                    };
                }

                if (!File.Exists(request.VideoPath))
                {
                    return new MergeSubtitlesResponse
                    {
                        Success = false,
                        Error = $"Fichier vidéo non trouvé: {request.VideoPath}"
                    };
                }

                // Vérifier FFmpeg
                if (!await FfmpegHelper.IsAvailableAsync())
                {
                    return new MergeSubtitlesResponse
                    {
                        Success = false,
                        Error = "FFmpeg n'est pas disponible. Installez FFmpeg et assurez-vous qu'il est dans le PATH."
                    };
                }

                var videoDir = Path.GetDirectoryName(request.VideoPath) ?? ".";
                var videoName = Path.GetFileNameWithoutExtension(request.VideoPath);
                var tempDir = Path.Combine(Path.GetTempPath(), "EmbySubtitleMerger");
                Directory.CreateDirectory(tempDir);

                // Obtenir le premier sous-titre (externe ou embarqué)
                string primary1Path;
                if (request.Primary1IsExternal && !string.IsNullOrEmpty(request.Primary1Path))
                {
                    // Sous-titre externe : utiliser directement le fichier
                    primary1Path = request.Primary1Path;
                    if (!File.Exists(primary1Path))
                    {
                        return new MergeSubtitlesResponse
                        {
                            Success = false,
                            Error = $"Fichier sous-titre 1 non trouvé: {primary1Path}"
                        };
                    }
                }
                else
                {
                    // Sous-titre embarqué : extraire avec FFmpeg
                    primary1Path = Path.Combine(tempDir, $"{videoName}.sub1.srt");
                    var result1 = await FfmpegHelper.ExtractSubtitleByAbsoluteIndexAsync(
                        request.VideoPath,
                        request.PrimaryIndex,
                        primary1Path);

                    if (!result1.Success)
                    {
                        return new MergeSubtitlesResponse
                        {
                            Success = false,
                            Error = $"Erreur extraction sous-titre 1: {result1.Error}"
                        };
                    }
                }

                // Obtenir le second sous-titre (externe ou embarqué)
                string secondary2Path;
                if (request.Primary2IsExternal && !string.IsNullOrEmpty(request.Primary2Path))
                {
                    // Sous-titre externe : utiliser directement le fichier
                    secondary2Path = request.Primary2Path;
                    if (!File.Exists(secondary2Path))
                    {
                        return new MergeSubtitlesResponse
                        {
                            Success = false,
                            Error = $"Fichier sous-titre 2 non trouvé: {secondary2Path}"
                        };
                    }
                }
                else
                {
                    // Sous-titre embarqué : extraire avec FFmpeg
                    secondary2Path = Path.Combine(tempDir, $"{videoName}.sub2.srt");
                    var result2 = await FfmpegHelper.ExtractSubtitleByAbsoluteIndexAsync(
                        request.VideoPath,
                        request.SecondaryIndex,
                        secondary2Path);

                    if (!result2.Success)
                    {
                        return new MergeSubtitlesResponse
                        {
                            Success = false,
                            Error = $"Erreur extraction sous-titre 2: {result2.Error}"
                        };
                    }
                }

                var outputPath = Path.Combine(videoDir, $"{videoName}.dual.srt");

                // Déterminer quels fichiers sont temporaires (à nettoyer après)
                bool sub1IsTemp = !request.Primary1IsExternal;
                bool sub2IsTemp = !request.Primary2IsExternal;

                // Choisir entre API cloud ou fusion locale
                if (request.UseCloudApi)
                {
                    // Verifier que la cle API est fournie
                    if (string.IsNullOrEmpty(request.DoubleSubApiKey))
                    {
                        return new MergeSubtitlesResponse
                        {
                            Success = false,
                            Error = "Cle API DoubleSub.io requise pour le mode cloud. Obtenez-en une sur doublesub.io/api/v1/docs"
                        };
                    }

                    // ========== FUSION VIA API DOUBLESUB.IO ==========
                    return await MergeViaCloudApiAsync(
                        primary1Path,
                        secondary2Path,
                        outputPath,
                        request.Mode ?? "all",
                        request.ToleranceMs ?? 700,
                        request.Color1,
                        request.Color2,
                        request.DoubleSubApiKey,
                        sub1IsTemp,
                        sub2IsTemp);
                }
                else
                {
                    // ========== FUSION LOCALE ==========
                    return await MergeLocallyAsync(
                        primary1Path,
                        secondary2Path,
                        outputPath,
                        request.Mode,
                        request.ToleranceMs ?? 700,
                        request.Color1,
                        request.Color2,
                        request.Offset1Ms,
                        request.Offset2Ms,
                        sub1IsTemp,
                        sub2IsTemp);
                }
            }
            catch (Exception ex)
            {
                return new MergeSubtitlesResponse
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// Fusionne via l'API DoubleSub.io (cloud)
        /// </summary>
        private async Task<MergeSubtitlesResponse> MergeViaCloudApiAsync(
            string srt1Path,
            string srt2Path,
            string outputPath,
            string mode,
            int toleranceMs,
            string? color1,
            string? color2,
            string apiKey,
            bool srt1IsTemp = true,
            bool srt2IsTemp = true)
        {
            try
            {
                using var apiClient = new DoubleSubApiClient(apiKey);

                // Vérifier que l'API est disponible
                if (!await apiClient.IsAvailableAsync())
                {
                    return new MergeSubtitlesResponse
                    {
                        Success = false,
                        Error = "L'API DoubleSub.io n'est pas accessible. Vérifiez votre connexion internet ou utilisez la fusion locale."
                    };
                }

                // Appeler l'API
                var result = await apiClient.MergeAsync(
                    srt1Path,
                    srt2Path,
                    outputPath,
                    mode,
                    toleranceMs,
                    color1,
                    color2);

                // Nettoyer uniquement les fichiers temporaires (pas les externes!)
                if (srt1IsTemp) CleanupTempFiles(srt1Path);
                if (srt2IsTemp) CleanupTempFiles(srt2Path);

                if (result.Success)
                {
                    return new MergeSubtitlesResponse
                    {
                        Success = true,
                        Message = $"Fusion cloud réussie! {result.CueCount} sous-titres créés via DoubleSub.io",
                        OutputPath = result.OutputPath,
                        CueCount = result.CueCount
                    };
                }
                else
                {
                    return new MergeSubtitlesResponse
                    {
                        Success = false,
                        Error = $"Erreur API cloud: {result.Error}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new MergeSubtitlesResponse
                {
                    Success = false,
                    Error = $"Erreur lors de l'appel API cloud: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Fusionne localement (sans API)
        /// </summary>
        private Task<MergeSubtitlesResponse> MergeLocallyAsync(
            string srt1Path,
            string srt2Path,
            string outputPath,
            string? mode,
            int toleranceMs,
            string? color1,
            string? color2,
            int offset1Ms = 0,
            int offset2Ms = 0,
            bool srt1IsTemp = true,
            bool srt2IsTemp = true)
        {
            try
            {
                // Parser les sous-titres
                var primary = SrtParser.ParseFile(srt1Path);
                var secondary = SrtParser.ParseFile(srt2Path);

                // Appliquer les offsets si necessaire
                if (offset1Ms != 0)
                {
                    foreach (var cue in primary)
                    {
                        cue.StartTime = cue.StartTime.Add(TimeSpan.FromMilliseconds(offset1Ms));
                        cue.EndTime = cue.EndTime.Add(TimeSpan.FromMilliseconds(offset1Ms));
                    }
                }
                if (offset2Ms != 0)
                {
                    foreach (var cue in secondary)
                    {
                        cue.StartTime = cue.StartTime.Add(TimeSpan.FromMilliseconds(offset2Ms));
                        cue.EndTime = cue.EndTime.Add(TimeSpan.FromMilliseconds(offset2Ms));
                    }
                }

                // Options de fusion
                var options = new MergeOptions
                {
                    ToleranceMs = toleranceMs,
                    UsePositioning = true,
                    PrimaryColor = color1,
                    SecondaryColor = color2
                };

                if (!string.IsNullOrEmpty(mode))
                {
                    options.Mode = mode.ToLower() switch
                    {
                        "overlapping" => MergeMode.OverlappingOnly,
                        "primary" => MergeMode.PrimaryPriority,
                        _ => MergeMode.AllCues
                    };
                }

                // Fusionner
                var merged = SubtitleMerger.Merge(primary, secondary, options);

                // Écrire le résultat
                SrtParser.WriteFile(outputPath, merged);

                // Nettoyer uniquement les fichiers temporaires (pas les externes!)
                if (srt1IsTemp) CleanupTempFiles(srt1Path);
                if (srt2IsTemp) CleanupTempFiles(srt2Path);

                return Task.FromResult(new MergeSubtitlesResponse
                {
                    Success = true,
                    Message = $"Fusion locale réussie! {merged.Count} sous-titres créés.",
                    OutputPath = outputPath,
                    CueCount = merged.Count
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new MergeSubtitlesResponse
                {
                    Success = false,
                    Error = $"Erreur fusion locale: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Nettoie les fichiers temporaires
        /// </summary>
        private void CleanupTempFiles(params string[] paths)
        {
            foreach (var path in paths)
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch { }
            }
        }
        
        private static string CreateHtmlResponse(string content)
        {
            return $"<html><head><meta charset='utf-8'></head><body style='margin:0;padding:10px;background:transparent;font-family:system-ui;color:#e6eaf0;'>{content}</body></html>";
        }
    }

    /// <summary>
    /// Service pour la page de fusion rapide
    /// </summary>
    public class QuickMergePageService : IService
    {
        public async Task<object> Get(QuickMergePageRequest request)
        {
            try
            {
                var itemId = request?.ItemId;
                if (string.IsNullOrEmpty(itemId))
                {
                    return GenerateQuickMergePage(null, null, new List<SubtitleInfo>());
                }

                var apiKey = "a482e26698584ef8a83d6ff49c0d8676";
                var url = $"http://localhost:8096/emby/Items/{itemId}?Fields=MediaStreams,Path&api_key={apiKey}";

                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var json = await client.GetStringAsync(url);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var name = root.TryGetProperty("Name", out var n) ? n.GetString() : "Film";
                var path = root.TryGetProperty("Path", out var p) ? p.GetString() : "";

                var subtitles = new List<SubtitleInfo>();
                if (root.TryGetProperty("MediaStreams", out var streams))
                {
                    foreach (var stream in streams.EnumerateArray())
                    {
                        var type = stream.TryGetProperty("Type", out var t) ? t.GetString() : "";
                        if (type != "Subtitle") continue;

                        subtitles.Add(new SubtitleInfo
                        {
                            Index = stream.TryGetProperty("Index", out var idx) ? idx.GetInt32() : -1,
                            Language = stream.TryGetProperty("Language", out var l) ? l.GetString() ?? "und" : "und",
                            Codec = stream.TryGetProperty("Codec", out var c) ? c.GetString() ?? "?" : "?",
                            IsExternal = stream.TryGetProperty("IsExternal", out var e) && e.GetBoolean(),
                            IsText = stream.TryGetProperty("IsTextSubtitleStream", out var txt) && txt.GetBoolean(),
                            Title = stream.TryGetProperty("Title", out var ti) ? ti.GetString() ?? "" : ""
                        });
                    }
                }

                return GenerateQuickMergePage(name, path, subtitles);
            }
            catch (Exception ex)
            {
                return $@"<html><head><meta charset='utf-8'></head>
                <body style='background:#1a1a2e;color:#fff;font-family:system-ui;padding:20px;'>
                <h2 style='color:#ff6b6b;'>Erreur</h2>
                <p>{WebUtility.HtmlEncode(ex.Message)}</p>
                </body></html>";
            }
        }

        private string GenerateQuickMergePage(string? movieName, string? moviePath, List<SubtitleInfo> subtitles)
        {
            var html = new StringBuilder();
            html.Append(@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Fusionner les sous-titres</title>
    <style>
        * { box-sizing: border-box; }
        body {
            font-family: system-ui, -apple-system, sans-serif;
            background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%);
            color: #e6eaf0;
            margin: 0;
            padding: 20px;
            min-height: 100vh;
        }
        .container { max-width: 600px; margin: 0 auto; }
        h1 { color: #00a4dc; font-size: 1.5em; margin: 0 0 5px 0; display: flex; align-items: center; gap: 10px; }
        h1::before { content: '🔀'; }
        .movie-name { color: #9fb0c2; font-size: 0.9em; margin-bottom: 20px; }
        .card { background: rgba(255,255,255,0.05); border-radius: 12px; padding: 20px; margin-bottom: 15px; border: 1px solid rgba(255,255,255,0.1); }
        .card h3 { color: #00a4dc; margin: 0 0 15px 0; font-size: 1em; }
        label { display: block; color: #9fb0c2; margin-bottom: 5px; font-size: 0.9em; }
        select, input {
            width: 100%; padding: 10px; background: rgba(0,0,0,0.3);
            border: 1px solid rgba(255,255,255,0.2); border-radius: 8px;
            color: #e6eaf0; font-size: 1em; margin-bottom: 15px;
        }
        select:focus, input:focus { outline: none; border-color: #00a4dc; }
        .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 15px; }
        .btn {
            width: 100%; padding: 14px; border: none; border-radius: 8px;
            font-weight: 600; cursor: pointer; font-size: 1em; transition: all 0.2s;
        }
        .btn-primary { background: linear-gradient(135deg, #00a4dc, #6366f1); color: white; }
        .btn-primary:hover { transform: translateY(-2px); box-shadow: 0 5px 20px rgba(0,164,220,0.4); }
        .btn-primary:disabled { opacity: 0.6; cursor: not-allowed; transform: none; }
        .result { padding: 12px; border-radius: 8px; margin-top: 15px; display: none; }
        .result.success { background: rgba(76,175,80,0.2); border-left: 4px solid #4CAF50; display: block; }
        .result.error { background: rgba(244,67,54,0.2); border-left: 4px solid #f44336; display: block; }
        .result.info { background: rgba(255,152,0,0.2); border-left: 4px solid #ff9800; display: block; }
        details { margin-bottom: 15px; }
        summary { color: #9fb0c2; cursor: pointer; padding: 10px; background: rgba(0,0,0,0.2); border-radius: 8px; }
        details[open] summary { border-radius: 8px 8px 0 0; }
        .options-content { background: rgba(0,0,0,0.2); padding: 15px; border-radius: 0 0 8px 8px; }
        .no-subs { color: #ff9800; text-align: center; padding: 30px; }
        .powered-by { text-align: center; margin-top: 20px; color: #666; font-size: 0.8em; }
        .powered-by a { color: #00a4dc; text-decoration: none; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>Fusionner les sous-titres</h1>");

            if (!string.IsNullOrEmpty(movieName))
            {
                html.Append($"<p class='movie-name'>{WebUtility.HtmlEncode(movieName)}</p>");
            }

            if (subtitles.Count < 2)
            {
                html.Append(@"
        <div class='card'>
            <div class='no-subs'>
                <p>⚠️ Il faut au moins 2 sous-titres pour pouvoir les fusionner.</p>
                <p style='font-size:0.9em;color:#888;'>Ajoutez des sous-titres externes ou assurez-vous que le fichier contient des sous-titres intégrés.</p>
            </div>
        </div>");
            }
            else
            {
                html.Append(@"
        <div class='card'>
            <h3>Sélectionner les sous-titres</h3>
            <div class='grid'>
                <div>
                    <label>Sous-titre 1 (HAUT)</label>
                    <select id='sub1'><option value=''>-- Choisir --</option>");

                foreach (var s in subtitles)
                {
                    var label = $"#{s.Index} {s.Language.ToUpper()} ({s.Codec})";
                    label += s.IsExternal ? " [EXT]" : " [INT]";
                    if (!s.IsText) label += " [IMG]";
                    html.Append($"<option value='{s.Index}' data-text='{s.IsText}'>{WebUtility.HtmlEncode(label)}</option>");
                }

                html.Append(@"</select>
                </div>
                <div>
                    <label>Sous-titre 2 (BAS)</label>
                    <select id='sub2'><option value=''>-- Choisir --</option>");

                foreach (var s in subtitles)
                {
                    var label = $"#{s.Index} {s.Language.ToUpper()} ({s.Codec})";
                    label += s.IsExternal ? " [EXT]" : " [INT]";
                    if (!s.IsText) label += " [IMG]";
                    html.Append($"<option value='{s.Index}' data-text='{s.IsText}'>{WebUtility.HtmlEncode(label)}</option>");
                }

                html.Append(@"</select>
                </div>
            </div>

            <details>
                <summary>Options avancées</summary>
                <div class='options-content'>
                    <div class='grid'>
                        <div>
                            <label>Mode de fusion</label>
                            <select id='mode'>
                                <option value='all'>Tous les sous-titres</option>
                                <option value='overlapping'>Chevauchement uniquement</option>
                                <option value='primary'>Priorité langue 1</option>
                            </select>
                        </div>
                        <div>
                            <label>Tolérance (ms)</label>
                            <input type='number' id='tolerance' value='700' min='0' max='5000' step='100'>
                        </div>
                    </div>
                </div>
            </details>

            <button id='btnMerge' class='btn btn-primary' onclick='doMerge()'>
                Fusionner les sous-titres
            </button>

            <div id='result' class='result'></div>
        </div>");
            }

            // Script JavaScript - use StringBuilder to avoid escaping issues
            var pathEscaped = moviePath != null ? moviePath.Replace("\\", "\\\\").Replace("'", "\\'") : "";
            html.Append("<div class='powered-by'>Powered by <a href='https://doublesub.io' target='_blank'>DoubleSub.io</a></div></div>");
            html.Append("<script>");
            html.Append("var videoPath = '").Append(pathEscaped).Append("';");
            html.Append(@"
function doMerge() {
    var sub1 = document.getElementById('sub1').value;
    var sub2 = document.getElementById('sub2').value;
    var result = document.getElementById('result');
    var btn = document.getElementById('btnMerge');
    if (!sub1 || !sub2) { showResult('error', 'Selectionnez 2 sous-titres'); return; }
    if (sub1 === sub2) { showResult('error', 'Choisissez 2 sous-titres differents'); return; }
    btn.disabled = true;
    btn.textContent = 'Fusion en cours...';
    showResult('info', 'Traitement en cours...');
    var mode = document.getElementById('mode').value;
    var tolerance = parseInt(document.getElementById('tolerance').value) || 700;
    fetch('/EmbySubtitleMerger/Merge?api_key=a482e26698584ef8a83d6ff49c0d8676', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            VideoPath: videoPath,
            PrimaryIndex: parseInt(sub1),
            SecondaryIndex: parseInt(sub2),
            Mode: mode,
            ToleranceMs: tolerance,
            UseCloudApi: false
        })
    })
    .then(function(r) { return r.json(); })
    .then(function(data) {
        btn.disabled = false;
        btn.textContent = 'Fusionner les sous-titres';
        if (data.Success) {
            showResult('success', 'Fusion reussie! ' + data.CueCount + ' sous-titres crees. ' + data.OutputPath);
        } else {
            showResult('error', 'Erreur: ' + data.Error);
        }
    })
    .catch(function(err) {
        btn.disabled = false;
        btn.textContent = 'Fusionner les sous-titres';
        showResult('error', 'Erreur: ' + err.message);
    });
}
function showResult(type, msg) {
    var el = document.getElementById('result');
    el.className = 'result ' + type;
    el.innerHTML = msg;
}
");
            html.Append("</script></body></html>");

            return html.ToString();
        }
    }

    /// <summary>
    /// Info d'un sous-titre
    /// </summary>
    public class SubtitleInfo
    {
        public int Index { get; set; }
        public string Language { get; set; } = "und";
        public string Codec { get; set; } = "?";
        public bool IsExternal { get; set; }
        public bool IsText { get; set; }
        public string Title { get; set; } = "";
    }
}


