using System;
using System.IO;
using System.Text;
using MediaBrowser.Model.Services;

namespace EmbySubtitleMerger
{
    /// <summary>
    /// Sert la page HTML complète du Subtitle Merger
    /// </summary>
    [Route("/SubtitleMerger", "GET")]
    [Route("/SubtitleMerger/Page", "GET")]
    public class SubtitleMergerPageRequest : IReturn<string>
    {
    }

    public class SubtitleMergerPageService : IService
    {
        public object Get(SubtitleMergerPageRequest request)
        {
            var html = GenerateFullPage();

            // Retourner le HTML directement
            // Emby devrait détecter que c'est du HTML et définir le bon Content-Type
            return html;
        }

        private string GenerateFullPage()
        {
            return @"<!DOCTYPE html>
<html lang=""fr"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Subtitle Merger - Emby Plugin</title>
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            font-family: 'Segoe UI', system-ui, sans-serif;
            background: linear-gradient(135deg, #0f1419 0%, #1a2634 50%, #0d1117 100%);
            color: #e6eaf0;
            min-height: 100vh;
            padding: 20px;
        }
        .container { max-width: 900px; margin: 0 auto; }
        h1 {
            color: #00a4dc;
            margin-bottom: 10px;
            display: flex;
            align-items: center;
            gap: 15px;
        }
        h1 span { font-size: 0.4em; color: #4CAF50; }
        h2 {
            color: #9fb0c2;
            font-size: 1.1em;
            margin: 25px 0 15px 0;
            padding-bottom: 8px;
            border-bottom: 1px solid #2b3743;
        }
        .card {
            background: rgba(23, 28, 34, 0.9);
            border-radius: 12px;
            padding: 25px;
            margin-bottom: 20px;
            box-shadow: 0 8px 32px rgba(0,0,0,0.3);
        }
        select, input, button { font-family: inherit; font-size: 14px; }
        select {
            width: 100%;
            padding: 12px;
            background: #0f1419;
            color: #e6eaf0;
            border: 1px solid #2b3743;
            border-radius: 8px;
            cursor: pointer;
        }
        select:focus { border-color: #00a4dc; outline: none; }
        .sub-row { display: flex; gap: 20px; flex-wrap: wrap; }
        .sub-col { flex: 1; min-width: 280px; }
        .sub-col label { display: block; margin-bottom: 8px; font-weight: 600; }
        .sub-col.primary label { color: #4CAF50; }
        .sub-col.secondary label { color: #2196F3; }
        .sub-col.primary select { border-color: #4CAF50; }
        .sub-col.secondary select { border-color: #2196F3; }
        .options {
            display: flex; gap: 20px; flex-wrap: wrap; margin-top: 20px;
            padding: 15px; background: rgba(0,0,0,0.3); border-radius: 8px;
        }
        .options label { color: #888; font-size: 0.9em; }
        .options select, .options input { margin-top: 5px; padding: 8px; }
        .options input[type=""number""] {
            width: 80px; background: #0f1419; color: #e6eaf0;
            border: 1px solid #2b3743; border-radius: 5px;
        }
        button.primary {
            background: linear-gradient(135deg, #4CAF50, #45a049);
            color: white; border: none; padding: 14px 28px;
            border-radius: 8px; cursor: pointer; font-weight: 600; font-size: 16px;
            transition: transform 0.2s, box-shadow 0.2s;
        }
        button.primary:hover {
            transform: translateY(-2px);
            box-shadow: 0 4px 15px rgba(76, 175, 80, 0.4);
        }
        button.primary:disabled {
            background: #555; cursor: not-allowed; transform: none; box-shadow: none;
        }
        button.secondary {
            background: #2b3743; color: #9fb0c2; border: 1px solid #3d4f5f;
            padding: 10px 20px; border-radius: 6px; cursor: pointer;
        }
        button.secondary:hover { background: #3d4f5f; }
        .preview {
            padding: 15px; background: rgba(0, 164, 220, 0.1);
            border-left: 4px solid #00a4dc; border-radius: 8px; margin: 20px 0;
        }
        .result { padding: 15px; border-radius: 8px; margin-top: 15px; }
        .result.success { background: rgba(76, 175, 80, 0.2); border-left: 4px solid #4CAF50; }
        .result.error { background: rgba(244, 67, 54, 0.2); border-left: 4px solid #f44336; }
        .status {
            display: flex; align-items: center; gap: 10px;
            padding: 10px 15px; background: rgba(0,0,0,0.3);
            border-radius: 6px; font-size: 0.85em; color: #888;
        }
        .status.ok { color: #4CAF50; }
        .status.error { color: #f44336; }
        .loader {
            width: 20px; height: 20px; border: 2px solid #2b3743;
            border-top-color: #00a4dc; border-radius: 50%;
            animation: spin 1s linear infinite;
        }
        @keyframes spin { to { transform: rotate(360deg); } }
        .movie-info { font-size: 0.9em; color: #888; margin-top: 8px; font-family: monospace; }
        .sub-item {
            padding: 8px 12px; margin: 5px 0; background: rgba(0,0,0,0.3);
            border-radius: 5px; display: flex; align-items: center; gap: 10px;
        }
        .sub-item.text { border-left: 3px solid #4CAF50; }
        .sub-item.image { border-left: 3px solid #ff9800; }
        .badge { padding: 2px 6px; border-radius: 3px; font-size: 0.75em; font-weight: bold; }
        .badge.text { background: #4CAF50; color: white; }
        .badge.image { background: #ff9800; color: white; }
        .badge.ext { background: #2196F3; color: white; }
        .badge.int { background: #666; color: white; }
        .hidden { display: none; }
        .back-link {
            display: inline-block; margin-bottom: 15px; color: #00a4dc;
            text-decoration: none; font-size: 0.9em;
        }
        .back-link:hover { text-decoration: underline; }
    </style>
</head>
<body>
    <div class=""container"">
        <a href=""/web/index.html"" class=""back-link"">← Retour à Emby</a>
        
        <div class=""card"">
            <h1>🎬 Subtitle Merger <span>v5.0 - Plugin Emby</span></h1>
            <div id=""status"" class=""status"">
                <div class=""loader""></div>
                <span>Connexion à Emby...</span>
            </div>
        </div>

        <div class=""card"">
            <h2>1. Sélectionner un film ou épisode</h2>
            <select id=""movieSelect"">
                <option value="""">— Chargement des films... —</option>
            </select>
            <div id=""movieInfo"" class=""movie-info""></div>
        </div>

        <div id=""subtitlesCard"" class=""card hidden"">
            <h2>2. Sélectionner les sous-titres à fusionner</h2>
            <div id=""subtitlesList""></div>
            
            <div class=""sub-row"" style=""margin-top: 20px;"">
                <div class=""sub-col primary"">
                    <label>↑ Sous-titre HAUT (langue 1):</label>
                    <select id=""sub1Select""><option value="""">— Sélectionner —</option></select>
                </div>
                <div class=""sub-col secondary"">
                    <label>↓ Sous-titre BAS (langue 2):</label>
                    <select id=""sub2Select""><option value="""">— Sélectionner —</option></select>
                </div>
            </div>

            <div class=""options"">
                <div>
                    <label>Mode de fusion:</label><br>
                    <select id=""mergeMode"">
                        <option value=""all"">Tous les sous-titres</option>
                        <option value=""overlapping"">Chevauchements uniquement</option>
                        <option value=""primary"">Priorité langue 1</option>
                    </select>
                </div>
                <div>
                    <label>Tolérance (ms):</label><br>
                    <input type=""number"" id=""tolerance"" value=""700"" min=""0"" max=""5000"">
                </div>
            </div>
        </div>

        <div id=""mergeCard"" class=""card hidden"">
            <h2>3. Fusionner</h2>
            <div id=""preview"" class=""preview"">
                Sélectionnez deux sous-titres différents pour activer la fusion.
            </div>
            <div style=""display: flex; gap: 15px; align-items: center;"">
                <button id=""btnMerge"" class=""primary"" disabled>🔀 Fusionner les sous-titres</button>
                <button id=""btnRefresh"" class=""secondary"">🔄 Rafraîchir</button>
            </div>
            <div id=""result""></div>
        </div>
    </div>

    <script>
        var API_KEY = '';
        var USER_ID = '';
        var BASE_URL = window.location.origin;

        var movies = [];
        var currentMovie = null;
        var subtitles = [];

        document.addEventListener('DOMContentLoaded', init);

        function init() {
            document.getElementById('movieSelect').addEventListener('change', onMovieChange);
            document.getElementById('sub1Select').addEventListener('change', updatePreview);
            document.getElementById('sub2Select').addEventListener('change', updatePreview);
            document.getElementById('btnMerge').addEventListener('click', doMerge);
            document.getElementById('btnRefresh').addEventListener('click', function() { location.reload(); });

            // Récupérer l'auth puis charger les données
            getAuthInfo().then(function() {
                checkStatus();
                loadMovies();
            });
        }

        function getAuthInfo() {
            // Récupérer depuis l'URL
            var params = new URLSearchParams(window.location.search);
            API_KEY = params.get('api_key') || params.get('ApiKey') || '';

            // Essayer de récupérer l'utilisateur courant depuis l'API
            return fetch(BASE_URL + '/emby/Users/Public')
                .then(function(res) { return res.json(); })
                .then(function(users) {
                    console.log('[SubtitleMerger] Users:', users);
                    if (users && users.length > 0) {
                        // Prendre le premier utilisateur ou celui en paramètre
                        var userParam = params.get('userId') || params.get('UserId');
                        if (userParam) {
                            USER_ID = userParam;
                        } else {
                            USER_ID = users[0].Id;
                        }
                        console.log('[SubtitleMerger] Using User ID:', USER_ID);
                    }
                })
                .catch(function(e) {
                    console.error('[SubtitleMerger] Error getting auth info:', e);
                    // Fallback vers les anciennes valeurs
                    API_KEY = API_KEY || 'a482e26698584ef8a83d6ff49c0d8676';
                    USER_ID = 'e69b7a5d73a943b4918712599f79905e';
                });
        }

        function checkStatus() {
            var statusEl = document.getElementById('status');
            fetch(BASE_URL + '/EmbySubtitleMerger/Status?api_key=' + API_KEY)
                .then(function(res) { return res.json(); })
                .then(function(data) {
                    if (data.FfmpegAvailable) {
                        statusEl.className = 'status ok';
                        statusEl.innerHTML = '✅ Connecté | FFmpeg: ' + (data.FfmpegVersion ? data.FfmpegVersion.split(' ')[2] : 'OK') + ' | Plugin v' + data.PluginVersion;
                    } else {
                        statusEl.className = 'status error';
                        statusEl.innerHTML = '⚠️ FFmpeg non disponible';
                    }
                })
                .catch(function(e) {
                    statusEl.className = 'status error';
                    statusEl.innerHTML = '❌ Erreur connexion: ' + e.message;
                });
        }

        function loadMovies() {
            var select = document.getElementById('movieSelect');

            if (!API_KEY || !USER_ID) {
                select.innerHTML = '<option value="""">⚠️ Erreur: API Key ou User ID manquant</option>';
                document.getElementById('status').innerHTML =
                    '<div style=""background:#ff6b6b;padding:15px;border-radius:8px;color:white;"">' +
                    '<strong>❌ Authentification requise</strong><br><br>' +
                    'Pour utiliser ce plugin, vous devez créer une <strong>clé API</strong>:<br>' +
                    '1. Allez dans <strong>Paramètres → Clés API</strong><br>' +
                    '2. Créez une nouvelle clé (nom: SubtitleMerger)<br>' +
                    '3. Ouvrez cette page avec: <code style=""background:rgba(0,0,0,0.3);padding:2px 5px;border-radius:3px;"">http://localhost:8096/SubtitleMerger?api_key=VOTRE_CLE</code>' +
                    '</div>';
                return;
            }

            var url = BASE_URL + '/emby/Users/' + USER_ID + '/Items?IncludeItemTypes=Movie,Episode&Recursive=true&SortBy=SortName&Fields=Path,MediaStreams,SeriesName,ParentIndexNumber,IndexNumber&api_key=' + API_KEY;

            fetch(url)
                .then(function(res) { return res.json(); })
                .then(function(data) {
                    movies = data.Items || [];
                    select.innerHTML = '<option value="""">— Sélectionner (' + movies.length + ' éléments) —</option>';
                    
                    for (var i = 0; i < movies.length; i++) {
                        var m = movies[i];
                        var streams = m.MediaStreams || [];
                        var subs = 0;
                        for (var j = 0; j < streams.length; j++) {
                            if (streams[j].Type === 'Subtitle') subs++;
                        }
                        var subInfo = subs > 0 ? ' - ' + subs + ' sous-titre(s)' : ' - Pas de sous-titres';

                        var label = '';
                        if (m.SeriesName) {
                            // C'est un épisode
                            var season = m.ParentIndexNumber ? 'S' + String(m.ParentIndexNumber).padStart(2, '0') : '';
                            var episode = m.IndexNumber ? 'E' + String(m.IndexNumber).padStart(2, '0') : '';
                            label = m.SeriesName + ' ' + season + episode + ' - ' + m.Name + subInfo;
                        } else {
                            // C'est un film
                            var year = m.ProductionYear ? ' (' + m.ProductionYear + ')' : '';
                            label = m.Name + year + subInfo;
                        }

                        var opt = document.createElement('option');
                        opt.value = i;
                        opt.textContent = label;
                        select.appendChild(opt);
                    }
                })
                .catch(function(e) {
                    select.innerHTML = '<option value="""">Erreur: ' + e.message + '</option>';
                });
        }

        function onMovieChange() {
            var idx = parseInt(this.value);
            var subtitlesCard = document.getElementById('subtitlesCard');
            var mergeCard = document.getElementById('mergeCard');
            var movieInfo = document.getElementById('movieInfo');
            
            if (isNaN(idx) || idx < 0) {
                subtitlesCard.classList.add('hidden');
                mergeCard.classList.add('hidden');
                movieInfo.textContent = '';
                return;
            }
            
            currentMovie = movies[idx];
            var pathParts = (currentMovie.Path || '').split(/[\\/]/);
            movieInfo.textContent = '📁 ' + pathParts[pathParts.length - 1];
            
            loadSubtitles(currentMovie.Id);
            
            subtitlesCard.classList.remove('hidden');
            mergeCard.classList.remove('hidden');
        }

        function loadSubtitles(itemId) {
            var listEl = document.getElementById('subtitlesList');
            var sub1 = document.getElementById('sub1Select');
            var sub2 = document.getElementById('sub2Select');

            console.log('[SubtitleMerger] Loading subtitles for itemId:', itemId);

            var url = BASE_URL + '/emby/Users/' + USER_ID + '/Items/' + itemId + '?Fields=MediaStreams,Path&api_key=' + API_KEY;

            fetch(url)
                .then(function(res) { return res.json(); })
                .then(function(data) {
                    console.log('[SubtitleMerger] API response:', data);
                    var streams = data.MediaStreams || [];
                    subtitles = [];
                    for (var i = 0; i < streams.length; i++) {
                        if (streams[i].Type === 'Subtitle') subtitles.push(streams[i]);
                    }

                    console.log('[SubtitleMerger] Found ' + subtitles.length + ' subtitle streams');

                    if (subtitles.length === 0) {
                        listEl.innerHTML = '<p style=""color:#ff6b6b;"">Aucun sous-titre trouvé.</p>';
                        sub1.innerHTML = sub2.innerHTML = '<option value="""">Aucun sous-titre</option>';
                        updatePreview();
                        return;
                    }
                    
                    var html = '';
                    var textSubs = [];
                    
                    for (var i = 0; i < subtitles.length; i++) {
                        var s = subtitles[i];
                        var codec = (s.Codec || '').toLowerCase();
                        var isText = s.IsTextSubtitleStream || codec === 'srt' || codec === 'ass' || codec === 'ssa' || codec === 'subrip' || codec === 'webvtt' || codec === 'mov_text';
                        var typeClass = isText ? 'text' : 'image';
                        var typeBadge = isText ? '<span class=""badge text"">TXT</span>' : '<span class=""badge image"">IMG</span>';
                        var locBadge = s.IsExternal ? '<span class=""badge ext"">EXT</span>' : '<span class=""badge int"">INT</span>';
                        
                        html += '<div class=""sub-item ' + typeClass + '"">' +
                            typeBadge + ' ' + locBadge +
                            ' <span>#' + s.Index + ' ' + (s.Language || 'und').toUpperCase() + ' (' + s.Codec + ')</span>' +
                            (s.Title ? '<span style=""color:#666;""> - ' + s.Title + '</span>' : '') +
                            (!isText ? '<span style=""color:#ff9800;font-size:0.8em;""> (nécessite OCR)</span>' : '') +
                            '</div>';
                        
                        if (isText) textSubs.push(s);
                    }
                    listEl.innerHTML = html;
                    
                    // Afficher TOUS les sous-titres dans les selects, mais désactiver les images
                    var options1 = '<option value="""">— Sélectionner —</option>';
                    var options2 = '<option value="""">— Sélectionner —</option>';

                    for (var i = 0; i < subtitles.length; i++) {
                        var s = subtitles[i];
                        var codec = (s.Codec || '').toLowerCase();
                        var isText = s.IsTextSubtitleStream || codec === 'srt' || codec === 'ass' || codec === 'ssa' || codec === 'subrip' || codec === 'webvtt' || codec === 'mov_text';
                        var label = '#' + s.Index + ' ' + (s.Language || 'und').toUpperCase() + ' (' + s.Codec + ')' + (s.Title ? ' - ' + s.Title : '');

                        if (!isText) {
                            label += ' [IMAGE - Non supporté]';
                        }

                        var disabled = !isText ? ' disabled' : '';
                        options1 += '<option value=""' + s.Index + '""' + disabled + '>' + label + '</option>';
                        options2 += '<option value=""' + s.Index + '""' + disabled + '>' + label + '</option>';
                    }

                    sub1.innerHTML = options1;
                    sub2.innerHTML = options2;

                    console.log('[SubtitleMerger] ' + textSubs.length + ' text subtitles, ' + subtitles.length + ' total');

                    if (textSubs.length === 0) {
                        document.getElementById('preview').innerHTML =
                            '<span style=""color:#ff9800;"">⚠️ Ce film n\'a que des sous-titres image (PGS/VobSub). ' +
                            'Vous devez convertir ces sous-titres en format texte (SRT) avec <strong>SubtitleEdit</strong> ou ajouter des fichiers .srt externes.</span>';
                    }
                    
                    updatePreview();
                })
                .catch(function(e) {
                    listEl.innerHTML = '<p style=""color:#ff6b6b;"">Erreur: ' + e.message + '</p>';
                });
        }

        function updatePreview() {
            var v1 = document.getElementById('sub1Select').value;
            var v2 = document.getElementById('sub2Select').value;
            var preview = document.getElementById('preview');
            var btn = document.getElementById('btnMerge');

            console.log('[SubtitleMerger] updatePreview - v1:', v1, 'v2:', v2);

            if (!v1 || !v2) {
                preview.innerHTML = 'Sélectionnez deux sous-titres pour activer la fusion.';
                btn.disabled = true;
                return;
            }

            if (v1 === v2) {
                preview.innerHTML = '<span style=""color:#ff6b6b;"">⚠️ Choisissez deux sous-titres différents !</span>';
                btn.disabled = true;
                return;
            }

            var s1 = null, s2 = null;
            for (var i = 0; i < subtitles.length; i++) {
                if (subtitles[i].Index == v1) s1 = subtitles[i];
                if (subtitles[i].Index == v2) s2 = subtitles[i];
            }

            console.log('[SubtitleMerger] Found subtitles - s1:', s1, 's2:', s2);

            preview.innerHTML = '<strong>Fusion prête:</strong><br>' +
                '<span style=""color:#4CAF50;"">↑ HAUT:</span> #' + v1 + ' ' + ((s1 && s1.Language) || '').toUpperCase() + ' (' + (s1 && s1.Codec) + ')<br>' +
                '<span style=""color:#2196F3;"">↓ BAS:</span> #' + v2 + ' ' + ((s2 && s2.Language) || '').toUpperCase() + ' (' + (s2 && s2.Codec) + ')';
            btn.disabled = false;
        }

        function doMerge() {
            var btn = document.getElementById('btnMerge');
            var result = document.getElementById('result');
            
            var v1 = document.getElementById('sub1Select').value;
            var v2 = document.getElementById('sub2Select').value;
            var mode = document.getElementById('mergeMode').value;
            var tolerance = document.getElementById('tolerance').value;
            
            if (!currentMovie || !v1 || !v2) return;
            
            btn.disabled = true;
            btn.textContent = '⏳ Fusion en cours...';
            result.innerHTML = '';
            
            var url = BASE_URL + '/EmbySubtitleMerger/Merge?api_key=' + API_KEY;
            var body = {
                VideoPath: currentMovie.Path,
                PrimaryIndex: parseInt(v1),
                SecondaryIndex: parseInt(v2),
                Mode: mode,
                ToleranceMs: parseInt(tolerance)
            };
            
            fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            })
            .then(function(res) { return res.json(); })
            .then(function(data) {
                if (data.Success) {
                    result.className = 'result success';
                    result.innerHTML = '<strong>✅ ' + data.Message + '</strong><br>' +
                        '<span style=""color:#888;"">Fichier: ' + data.OutputPath + '</span><br>' +
                        '<span style=""color:#888;"">' + data.CueCount + ' sous-titres fusionnés</span>';
                } else {
                    result.className = 'result error';
                    result.innerHTML = '<strong>❌ Erreur:</strong> ' + data.Error;
                }
            })
            .catch(function(e) {
                result.className = 'result error';
                result.innerHTML = '<strong>❌ Erreur:</strong> ' + e.message;
            })
            .finally(function() {
                btn.disabled = false;
                btn.textContent = '🔀 Fusionner les sous-titres';
            });
        }
    </script>
</body>
</html>";
        }
    }

}
