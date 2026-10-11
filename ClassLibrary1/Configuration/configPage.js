define([], function () {
    'use strict';

    console.log('[SubMerger] Module loaded v9.12.0');

    var allMovies = [];      // Tous les medias charges
    var filteredMovies = []; // Medias filtres
    var libraries = [];      // Liste des mediatheques
    var currentMovie = null;
    var currentMediaSource = null; // Version selectionnee (MediaSource)
    var useCloudApi = false;
    var currentTranslationJob = null;    // Id du job de traduction en cours
    var currentTranslationItemId = null; // Element Emby que ce job traduit
    var translationPollTimer = null;     // Timer de polling du statut
    var translationPollFailures = 0;     // Echecs consecutifs du polling
    var normalizeJob = null;             // Id de la tache de normalisation en cours
    var normalizeItemId = null;          // Element Emby vise, ou null pour toutes les mediatheques
    var normalizePollTimer = null;
    var normalizePollFailures = 0;
    var normalizeTarget = 'tab';         // Cartes qui affichent la tache : 'tab' (onglet Normaliser), 'library' (nettoyage de Maintenance), 'batch' (fusion en serie)
    var serverHasAnthropicKey = false;   // Une cle Anthropic est enregistree sur le serveur
    var normalizePreview = null;         // Derniere demande d'apercu : "Appliquer" la rejoue a l'identique
    var normalizeJoined = false;         // La tache suivie est celle d'une autre demande, deja en cours
    var normalizeBusy = false;           // Une normalisation ou un nettoyage est lance
    var normalizeBlocked = false;        // ffmpeg ou ffprobe manque sur le serveur
    var pageVisible = false;             // La page du plugin est affichee
    var mergeTimer = null;               // Minuteur du temps ecoule pendant une fusion
    var mergeJob = null;                 // Id de la fusion en cours cote serveur
    var mergeItemId = null;              // Element Emby de la fusion en cours
    var mergeStep = '';                  // Etape annoncee par le serveur
    var mergeStarted = 0;                // Debut de la fusion, pour le temps ecoule
    var mergePollTimer = null;
    var mergePollFailures = 0;

    // Contenus qui dependent du film selectionne, et le message affiche a leur place tant qu'aucun film n'est choisi
    var WORK_SECTIONS = ['#mergeContent', '#translateContent'];
    var WORK_HINTS = ['#mergeHint', '#translateHint'];

    function getApiClient() {
        return window.ApiClient;
    }

    // URL d'un endpoint du plugin, authentifiee avec le jeton de la session Emby
    function pluginUrl(name, query) {
        var apiClient = getApiClient();
        var url = apiClient.serverAddress() + '/EmbySubtitleMerger/' + name + '?api_key=' + apiClient.accessToken();
        return query ? url + '&' + query : url;
    }

    // Un proxy ou Emby peut repondre par une page d'erreur HTML : on le dit clairement
    // plutot que de laisser remonter une erreur d'analyse JSON.
    function readJson(response) {
        return response.text().then(function (text) {
            try {
                return JSON.parse(text);
            } catch (e) {
                throw new Error('reponse inattendue du serveur (HTTP ' + response.status + '). ' +
                    'La requete a peut-etre ete coupee avant la fin.');
            }
        });
    }

    function getPlugin(name, query) {
        return fetch(pluginUrl(name, query)).then(readJson);
    }

    function postPlugin(name, body) {
        return fetch(pluginUrl(name), {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        }).then(readJson);
    }

    function escapeHtml(text) {
        return String(text == null ? '' : text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }

    // kind: ok, error, warning ou info. html doit deja etre echappe.
    function statusBox(kind, html) {
        return '<div class="status-box status-' + kind + '">' + html + '</div>';
    }

    // Chemin de la version selectionnee si le film en a plusieurs, sinon celui du film
    function currentVideoPath() {
        return (currentMediaSource && currentMediaSource.Path) ? currentMediaSource.Path : currentMovie.Path;
    }

    function readTolerance(view) {
        return parseInt(view.querySelector('#tolerance').value) || 700;
    }

    function setWorkSectionsVisible(view, visible) {
        WORK_SECTIONS.forEach(function (selector) {
            var section = view.querySelector(selector);
            if (section) section.style.display = visible ? 'block' : 'none';
        });
        WORK_HINTS.forEach(function (selector) {
            var hint = view.querySelector(selector);
            if (hint) hint.style.display = visible ? 'none' : 'block';
        });
    }

    // Affiche un onglet et memorise ce choix pour la prochaine visite
    function selectTab(view, name) {
        var tabs = view.querySelectorAll('.merger-tab');
        var panels = view.querySelectorAll('.tab-panel');
        var i;

        for (i = 0; i < tabs.length; i++) {
            tabs[i].classList.toggle('active', tabs[i].dataset.tab === name);
        }
        for (i = 0; i < panels.length; i++) {
            panels[i].classList.toggle('active', panels[i].dataset.panel === name);
        }

        try { localStorage.setItem('submerger_tab', name); } catch (e) { }
    }

    function log(view, msg) {
        console.log('[SubMerger] ' + msg);
        var info = view.querySelector('#movieInfo');
        if (info) {
            info.textContent = msg;
            info.className = 'status-box status-info';
        }
    }

    function success(view, msg) {
        console.log('[SubMerger] SUCCESS: ' + msg);
        var info = view.querySelector('#movieInfo');
        if (info) {
            info.textContent = msg;
            info.className = 'status-box status-ok';
        }
    }

    function error(view, msg) {
        console.log('[SubMerger] ERROR: ' + msg);
        var info = view.querySelector('#movieInfo');
        if (info) {
            info.textContent = msg;
            info.className = 'status-box status-error';
        }
    }

    function loadLibraries(view) {
        var apiClient = getApiClient();
        if (!apiClient) return;

        var userId = apiClient.getCurrentUserId();
        var url = apiClient.getUrl('Users/' + userId + '/Views');

        apiClient.getJSON(url).then(function(data) {
            libraries = data.Items || [];
            var libSelect = view.querySelector('#libraryFilter');
            libSelect.innerHTML = '<option value="">Toutes les mediatheques</option>';

            for (var i = 0; i < libraries.length; i++) {
                var lib = libraries[i];
                // Seulement les mediatheques de films et series
                if (lib.CollectionType === 'movies' || lib.CollectionType === 'tvshows') {
                    var opt = document.createElement('option');
                    opt.value = lib.Id;
                    opt.textContent = lib.Name;
                    libSelect.appendChild(opt);
                }
            }
            console.log('[SubMerger] Libraries loaded:', libraries.length);
        }).catch(function(e) {
            console.error('[SubMerger] Error loading libraries:', e);
        });
    }

    function loadMovies(view) {
        log(view, 'Chargement en cours...');

        var apiClient = getApiClient();
        if (!apiClient) {
            error(view, 'ApiClient non disponible');
            return;
        }

        var userId = apiClient.getCurrentUserId();
        var typeFilter = view.querySelector('#typeFilter').value;
        var libraryFilter = view.querySelector('#libraryFilter').value;

        var params = {
            Recursive: true,
            SortBy: 'SortName',
            Fields: 'Path,MediaStreams,MediaSources,SeriesName,ParentIndexNumber,IndexNumber'
        };

        // Filtre par type
        if (typeFilter) {
            params.IncludeItemTypes = typeFilter;
        } else {
            params.IncludeItemTypes = 'Movie,Episode';
        }

        // Filtre par mediatheque
        if (libraryFilter) {
            params.ParentId = libraryFilter;
        }

        var url = apiClient.getUrl('Users/' + userId + '/Items', params);
        console.log('[SubMerger] URL: ' + url);

        apiClient.getJSON(url).then(function (data) {
            console.log('[SubMerger] Got response:', data);
            allMovies = data.Items || [];

            // Trier par ordre alphabetique
            allMovies.sort(function(a, b) {
                var labelA = getItemLabel(a).toLowerCase();
                var labelB = getItemLabel(b).toLowerCase();
                return labelA.localeCompare(labelB);
            });

            filteredMovies = allMovies.slice(); // Copie

            // Reset search
            view.querySelector('#searchFilter').value = '';

            updateMoviesList(view);
            success(view, allMovies.length + ' elements charges!');
        }).catch(function (e) {
            console.error('[SubMerger] Error:', e);
            error(view, 'Erreur: ' + (e.message || e));
        });
    }

    function filterMovies(view) {
        var searchText = view.querySelector('#searchFilter').value.toLowerCase().trim();

        if (!searchText) {
            filteredMovies = allMovies.slice();
        } else {
            filteredMovies = allMovies.filter(function(m) {
                var label = getItemLabel(m).toLowerCase();
                return label.indexOf(searchText) !== -1;
            });
        }

        updateMoviesList(view);
    }

    function getItemLabel(m) {
        if (m.SeriesName) {
            var season = m.ParentIndexNumber ? 'S' + String(m.ParentIndexNumber).padStart(2, '0') : '';
            var episode = m.IndexNumber ? 'E' + String(m.IndexNumber).padStart(2, '0') : '';
            return m.SeriesName + ' ' + season + episode + ' - ' + m.Name;
        } else {
            return m.Name + (m.ProductionYear ? ' (' + m.ProductionYear + ')' : '');
        }
    }

    function updateMoviesList(view) {
        var sel = view.querySelector('#moviesSelect');
        var countEl = view.querySelector('#itemCount');

        sel.innerHTML = '';

        if (filteredMovies.length === 0) {
            sel.innerHTML = '<option value="">-- Aucun resultat --</option>';
            countEl.textContent = '0 element';
            return;
        }

        for (var i = 0; i < filteredMovies.length; i++) {
            var m = filteredMovies[i];
            var opt = document.createElement('option');
            opt.value = i;
            opt.textContent = getItemLabel(m);
            sel.appendChild(opt);
        }

        countEl.textContent = filteredMovies.length + ' element(s) affiche(s)' +
            (filteredMovies.length !== allMovies.length ? ' sur ' + allMovies.length : '');
    }

    function onMovieChange(view) {
        var idx = parseInt(view.querySelector('#moviesSelect').value);
        if (isNaN(idx) || idx < 0 || idx >= filteredMovies.length) {
            view.querySelector('#versionSection').style.display = 'none';
            setWorkSectionsVisible(view, false);
            return;
        }

        currentMovie = filteredMovies[idx];
        currentMediaSource = null;
        log(view, 'Chargement des details...');

        // Recharger les donnees completes de l'item pour avoir toutes les MediaSources
        var apiClient = getApiClient();
        var userId = apiClient.getCurrentUserId();
        var url = apiClient.getUrl('Users/' + userId + '/Items/' + currentMovie.Id, {
            Fields: 'Path,MediaStreams,MediaSources,SeriesName,ParentIndexNumber,IndexNumber'
        });

        apiClient.getJSON(url).then(function(data) {
            // Mettre a jour les donnees dans les listes
            var aIdx = allMovies.findIndex(function(m) { return m.Id === data.Id; });
            if (aIdx >= 0) allMovies[aIdx] = data;
            var fIdx = filteredMovies.findIndex(function(m) { return m.Id === data.Id; });
            if (fIdx >= 0) filteredMovies[fIdx] = data;
            currentMovie = data;

            log(view, 'Selection: ' + getItemLabel(currentMovie));
            showMediaSources(view);
        }).catch(function(e) {
            console.error('[SubMerger] Error loading item details:', e);
            // Fallback: utiliser les donnees du chargement en masse
            log(view, 'Selection: ' + getItemLabel(currentMovie));
            showMediaSources(view);
        });
    }

    function showMediaSources(view) {
        var versionSection = view.querySelector('#versionSection');

        // Verifier si plusieurs versions existent
        var mediaSources = currentMovie.MediaSources || [];
        console.log('[SubMerger] MediaSources:', mediaSources.length, mediaSources);

        if (mediaSources.length > 1) {
            // Plusieurs versions - afficher le selecteur
            var versionSelect = view.querySelector('#versionSelect');
            versionSelect.innerHTML = '<option value="">-- Choisir une version --</option>';

            for (var v = 0; v < mediaSources.length; v++) {
                var ms = mediaSources[v];
                var versionLabel = ms.Name || ms.Path || ('Version ' + (v + 1));
                // Extraire juste le nom du fichier
                if (ms.Path) {
                    versionLabel = ms.Path.split(/[\\/]/).pop();
                }
                var opt = document.createElement('option');
                opt.value = v;
                opt.textContent = versionLabel;
                versionSelect.appendChild(opt);
            }

            versionSelect.onchange = function() {
                onVersionChange(view);
            };

            versionSection.style.display = 'block';
            setWorkSectionsVisible(view, false);
        } else {
            // Une seule version ou pas de MediaSources - charger directement
            versionSection.style.display = 'none';
            if (mediaSources.length === 1) {
                currentMediaSource = mediaSources[0];
            }
            loadSubtitlesForCurrentSource(view);
        }
    }

    function onVersionChange(view) {
        var versionSelect = view.querySelector('#versionSelect');
        var idx = parseInt(versionSelect.value);
        var mediaSources = currentMovie.MediaSources || [];

        if (isNaN(idx) || idx < 0 || idx >= mediaSources.length) {
            setWorkSectionsVisible(view, false);
            return;
        }

        currentMediaSource = mediaSources[idx];
        console.log('[SubMerger] Version selectionnee:', currentMediaSource.Path);
        loadSubtitlesForCurrentSource(view);
    }

    function loadSubtitlesForCurrentSource(view) {
        // Utiliser les MediaStreams de la MediaSource si disponible, sinon ceux du film
        var streams = [];
        if (currentMediaSource && currentMediaSource.MediaStreams) {
            streams = currentMediaSource.MediaStreams;
        } else if (currentMovie.MediaStreams) {
            streams = currentMovie.MediaStreams;
        }

        var subs = [];
        for (var i = 0; i < streams.length; i++) {
            var s = streams[i];
            if (s.Type === 'Subtitle') {
                var isText = s.IsTextSubtitleStream;
                var isExternal = s.IsExternal || false;
                var subPath = s.Path || '';
                var kind = describeTrack(s);
                var label = kind.mark + ' #' + s.Index + ' ';
                label += s.Language ? s.Language.toUpperCase() : 'UND';
                label += ' (' + (s.Codec || '?') + ')';
                label += isExternal ? ' [EXT]' : ' [INT]';
                if (!isText) label += ' [IMAGE]';
                label += ' : ' + kind.short;
                // Ajouter le nom du fichier pour les sous-titres externes
                if (isExternal && subPath) {
                    var fileName = subPath.split(/[\\/]/).pop();
                    if (fileName) label += ' - ' + fileName;
                }
                subs.push({
                    index: s.Index, label: label, isText: isText, isExternal: isExternal, path: subPath,
                    lang: s.Language || '', warning: kind.warning
                });
            }
        }

        var s1 = view.querySelector('#sub1');
        var s2 = view.querySelector('#sub2');
        var translateSource = view.querySelector('#translateSource');
        s1.innerHTML = '<option value="">-- Choisir --</option>';
        s2.innerHTML = '<option value="">-- Choisir --</option>';
        translateSource.innerHTML = '<option value="">-- Choisir --</option>';

        function makeOption(sub) {
            var option = document.createElement('option');
            option.value = sub.index;
            option.textContent = sub.label;
            option.dataset.isText = sub.isText;
            option.dataset.isExternal = sub.isExternal;
            option.dataset.path = sub.path;
            option.dataset.lang = sub.lang;
            option.dataset.warning = sub.warning;
            return option;
        }

        for (var j = 0; j < subs.length; j++) {
            s1.appendChild(makeOption(subs[j]));
            s2.appendChild(makeOption(subs[j]));
            translateSource.appendChild(makeOption(subs[j]));
        }

        s1.onchange = function() { checkTrackWarnings(view); };
        s2.onchange = function() { checkTrackWarnings(view); };
        translateSource.onchange = function() { checkTranslateSourceWarning(view); };
        checkTrackWarnings(view);
        checkTranslateSourceWarning(view);

        setWorkSectionsVisible(view, true);

        success(view, getItemLabel(currentMovie) + ' : ' + subs.length + ' sous-titre(s) trouve(s). Choisissez une action dans les onglets ci-dessous.');

        checkCloudStatus(view);
    }

    function refreshMovieMetadata(view) {
        if (!currentMovie) {
            error(view, 'Selectionnez d\'abord un element');
            return;
        }

        log(view, 'Rechargement...');
        reloadCurrentMovie(view);
    }

    function reloadCurrentMovie(view) {
        if (!currentMovie) return;

        var apiClient = getApiClient();
        var userId = apiClient.getCurrentUserId();
        var url = apiClient.getUrl('Users/' + userId + '/Items/' + currentMovie.Id, {
            Fields: 'Path,MediaStreams,MediaSources,SeriesName,ParentIndexNumber,IndexNumber'
        });

        apiClient.getJSON(url).then(function(data) {
            var idx = allMovies.findIndex(function(m) { return m.Id === data.Id; });
            if (idx >= 0) {
                allMovies[idx] = data;
            }
            var fIdx = filteredMovies.findIndex(function(m) { return m.Id === data.Id; });
            if (fIdx >= 0) {
                filteredMovies[fIdx] = data;
            }
            currentMovie = data;
            onMovieChange(view);
            success(view, 'Sous-titres actualises!');
        }).catch(function(e) {
            error(view, 'Erreur rechargement: ' + (e.message || e));
        });
    }

    function refreshItemMetadata(view, itemId) {
        var apiClient = getApiClient();
        var baseUrl = apiClient.serverAddress();
        var apiKey = apiClient.accessToken();

        // Appeler l'API Emby avec FullRefresh pour forcer la detection du nouveau sous-titre bilingue
        var refreshUrl = baseUrl + '/emby/Items/' + itemId + '/Refresh?Recursive=false&MetadataRefreshMode=FullRefresh&ImageRefreshMode=Default&ReplaceAllMetadata=false&ReplaceAllImages=false&api_key=' + apiKey;

        console.log('[SubMerger] Refreshing item metadata (FullRefresh):', itemId);

        return fetch(refreshUrl, { method: 'POST' })
            .then(function(response) {
                if (response.ok) {
                    console.log('[SubMerger] Item metadata refresh triggered');
                    // Attendre qu'Emby traite le refresh et detecte le nouveau sous-titre
                    return new Promise(function(resolve) {
                        setTimeout(resolve, 4000);
                    });
                } else {
                    console.error('[SubMerger] Refresh failed:', response.status);
                    return Promise.resolve();
                }
            })
            .catch(function(e) {
                console.error('[SubMerger] Refresh error:', e);
                return Promise.resolve();
            });
    }

    // Ce que la fusion et la traduction peuvent faire de chaque type de piste.
    // mark : [OK] utilisable, [!] utilisable avec une reserve, [X] non utilisable.
    function describeTrack(stream) {
        var codec = String(stream.Codec || '').toLowerCase();
        var external = !!stream.IsExternal;
        var info;

        if (!stream.IsTextSubtitleStream) {
            // Le detail est donne par l'encadre #imageWarning
            info = { mark: '[X]', short: 'image, pas de texte a fusionner', warning: '' };
        } else if (codec === 'ass' || codec === 'ssa') {
            // Le serveur normalise la piste le temps de l'operation : conversion en SRT, codes de style retires
            info = { mark: '[OK]', short: 'ASS, normalise automatiquement a la fusion', warning: '' };
        } else if (codec === 'srt' || codec === 'subrip') {
            info = { mark: '[OK]', short: 'pret pour la fusion', warning: '' };
        } else if (!external) {
            // MOV_TEXT, WEBVTT... : texte integre, converti a l'extraction
            info = { mark: '[OK]', short: 'converti en SRT a la fusion', warning: '' };
        } else {
            info = { mark: '[!]', short: 'format externe, converti a la fusion',
                warning: 'fichier externe au format ' + (stream.Codec || 'inconnu') + ' : il sera converti en SRT le temps de l\'operation, mais ce format n\'a pas ete verifie. Controlez le resultat.' };
        }

        var forced = !!stream.IsForced || /forced/i.test((stream.Title || '') + ' ' + (stream.DisplayTitle || ''));
        if (forced && info.mark !== '[X]') {
            info.mark = '[!]';
            info.short += ', forced';
            info.warning = 'piste "forced" : elle ne contient que quelques repliques (passages en langue etrangere). Le bilingue sera presque monolingue.'
                + (info.warning ? ' De plus, ' + info.warning : '');
        }

        return info;
    }

    // Affiche les reserves des deux pistes choisies sous les listes
    function checkTrackWarnings(view) {
        var selects = [view.querySelector('#sub1'), view.querySelector('#sub2')];
        var imageWarning = view.querySelector('#imageWarning');
        var trackWarning = view.querySelector('#trackWarning');

        var hasImage = false;
        var messages = [];

        selects.forEach(function (select, position) {
            var option = select.options[select.selectedIndex];
            if (!option || !option.value) return;

            if (option.dataset.isText === 'false') hasImage = true;
            if (option.dataset.warning) {
                messages.push('<strong>Sous-titre ' + (position + 1) + '</strong> - ' + escapeHtml(option.dataset.warning));
            }
        });

        if (imageWarning) imageWarning.style.display = hasImage ? 'block' : 'none';
        if (trackWarning) {
            trackWarning.innerHTML = messages.join('<br><br>');
            trackWarning.style.display = messages.length ? 'block' : 'none';
        }
    }


    function checkCloudStatus(view) {
        var cloudStatus = view.querySelector('#cloudStatus');

        getPlugin('Status')
            .then(function(data) {
                console.log('[SubMerger] Status:', data);
                if (data.CloudApiAvailable) {
                    cloudStatus.textContent = 'DoubleSub.io disponible';
                    cloudStatus.className = 'status-box status-ok';
                } else {
                    cloudStatus.textContent = 'DoubleSub.io non disponible - utilisez la fusion locale';
                    cloudStatus.className = 'status-box status-warning';
                }
            })
            .catch(function() {
                cloudStatus.textContent = 'Impossible de verifier DoubleSub.io';
                cloudStatus.className = 'status-box status-error';
            });
    }

    function selectMode(view, mode) {
        var modeLocal = view.querySelector('#modeLocal');
        var modeCloud = view.querySelector('#modeCloud');
        var cloudStatus = view.querySelector('#cloudStatus');
        var apiKeySection = view.querySelector('#apiKeySection');

        if (mode === 'local') {
            useCloudApi = false;
            modeLocal.classList.add('active');
            modeCloud.classList.remove('active');
            cloudStatus.style.display = 'none';
            apiKeySection.style.display = 'none';
        } else {
            useCloudApi = true;
            modeLocal.classList.remove('active');
            modeCloud.classList.add('active');
            cloudStatus.style.display = 'block';
            apiKeySection.style.display = 'block';
            var savedKey = localStorage.getItem('doublesub_api_key');
            if (savedKey) {
                view.querySelector('#apiKey').value = savedKey;
            }
        }

        // DoubleSub.io decide seul de la disposition et n'applique pas les decalages
        ['#layout', '#offset1', '#offset2'].forEach(function (selector) {
            view.querySelector(selector).disabled = useCloudApi;
        });

        console.log('[SubMerger] Mode selected: ' + (useCloudApi ? 'DoubleSub.io' : 'Local'));
    }

    // Message affiche a cote du bouton concerne, dans la zone de resultat de l'onglet
    function showMessage(view, boxSelector, kind, message) {
        console.log('[SubMerger] ' + kind + ': ' + message);
        view.querySelector(boxSelector).innerHTML = statusBox(kind, escapeHtml(message));
    }

    // Affiche le fichier produit, demande a Emby de le prendre en compte, puis recharge les pistes
    function showProducedThenRefresh(view, boxSelector, itemId, html) {
        var result = view.querySelector(boxSelector);
        result.innerHTML = statusBox('ok', html + '<br><em>Actualisation des metadonnees...</em>');

        refreshItemMetadata(view, itemId).then(function() {
            result.innerHTML = statusBox('ok', html + '<br><em>Actualisation demandee : le sous-titre devrait apparaitre dans Emby.</em>');
            // Ne pas ecraser la selection si l'utilisateur est passe a un autre film entre-temps
            if (currentMovie && currentMovie.Id === itemId) {
                reloadCurrentMovie(view);
            }
        });
    }

    // La fusion tourne en tache de fond sur le serveur : l'extraction d'une piste d'un gros film
    // dure plusieurs minutes. La barre affiche l'etape annoncee par le serveur et le temps ecoule.
    function setMergeRunning(view, running) {
        var label = view.querySelector('#mergeProgressLabel');

        view.querySelector('#btnMerge').disabled = running;
        view.querySelector('#mergeProgress').style.display = running ? 'block' : 'none';
        clearInterval(mergeTimer);

        if (running) {
            var update = function () {
                var seconds = Math.round((Date.now() - mergeStarted) / 1000);
                label.textContent = (mergeStep || 'Fusion en cours') + '... ' + seconds + ' s' +
                    (seconds >= 30 ? ' (plusieurs minutes pour un gros film)' : '');
            };
            update();
            mergeTimer = setInterval(update, 1000);
        }
    }

    function finishMerge(view, data) {
        var itemId = mergeItemId;

        mergeJob = null;
        clearTimeout(mergePollTimer);
        mergePollTimer = null;
        setMergeRunning(view, false);

        if (data.Success) {
            showProducedThenRefresh(view, '#result', itemId,
                '<strong>' + escapeHtml(data.Message) + '</strong><br><small>' + escapeHtml(data.OutputPath) + '</small>');
        } else {
            showMessage(view, '#result', 'error', data.Error);
        }
    }

    function cancelMerge() {
        if (!mergeJob) return;

        postPlugin('MergeCancel', { JobId: mergeJob }).catch(function (e) {
            console.error('[SubMerger] Merge cancel error:', e);
        });
    }

    function pollMerge(view) {
        // Un seul suivi a la fois, meme apres un aller-retour rapide hors de la page
        clearTimeout(mergePollTimer);
        if (!mergeJob || !pageVisible) return;

        getPlugin('MergeStatus', 'JobId=' + encodeURIComponent(mergeJob))
            .then(function (data) {
                mergePollFailures = 0;
                if (!mergeJob) return;

                if (data.Running) {
                    mergeStep = data.Step || mergeStep;
                    mergePollTimer = setTimeout(function () { pollMerge(view); }, 2000);
                } else {
                    console.log('[SubMerger] Merge response:', data);
                    finishMerge(view, data);
                }
            })
            .catch(function (e) {
                console.error('[SubMerger] Merge status error:', e);
                if (++mergePollFailures >= 10) {
                    finishMerge(view, { Success: false, Error: 'Suivi de la fusion impossible : ' + e.message +
                        ' La fusion continue peut-etre sur le serveur : rechargez les pistes dans quelques minutes.' });
                } else {
                    mergePollTimer = setTimeout(function () { pollMerge(view); }, 3000);
                }
            });
    }

    function doMerge(view) {
        var sub1El = view.querySelector('#sub1');
        var sub2El = view.querySelector('#sub2');
        var v1 = sub1El.value;
        var v2 = sub2El.value;

        if (!currentMovie || !v1 || !v2) {
            showMessage(view, '#result', 'warning', 'Choisissez les deux sous-titres a fusionner.');
            return;
        }
        if (v1 === v2) {
            showMessage(view, '#result', 'warning', 'Choisissez deux sous-titres differents.');
            return;
        }

        var sel1 = sub1El.options[sub1El.selectedIndex];
        var sel2 = sub2El.options[sub2El.selectedIndex];


        var doublesubApiKey = '';
        if (useCloudApi) {
            doublesubApiKey = view.querySelector('#apiKey').value.trim();
            if (!doublesubApiKey) {
                showMessage(view, '#result', 'warning', 'Cle API DoubleSub.io requise pour le mode cloud (dans les options avancees).');
                return;
            }
            if (!doublesubApiKey.startsWith('dsub_')) {
                showMessage(view, '#result', 'warning', 'Cle API DoubleSub.io invalide (elle doit commencer par dsub_).');
                return;
            }
            localStorage.setItem('doublesub_api_key', doublesubApiKey);
        }


        mergeItemId = currentMovie.Id;
        mergeStep = '';
        mergeStarted = Date.now();
        mergePollFailures = 0;

        view.querySelector('#result').innerHTML = '';
        setMergeRunning(view, true);

        postPlugin('Merge', {
            VideoPath: currentVideoPath(),
            PrimaryIndex: parseInt(v1),
            SecondaryIndex: parseInt(v2),
            Primary1IsExternal: sel1.dataset.isExternal === 'true',
            Primary1Path: sel1.dataset.path || '',
            Primary2IsExternal: sel2.dataset.isExternal === 'true',
            Primary2Path: sel2.dataset.path || '',
            Language1: sel1.dataset.lang || '',
            Language2: sel2.dataset.lang || '',
            Mode: view.querySelector('#mergeMode').value,
            ToleranceMs: readTolerance(view),
            Offset1Ms: parseInt(view.querySelector('#offset1').value) || 0,
            Offset2Ms: parseInt(view.querySelector('#offset2').value) || 0,
            Layout: view.querySelector('#layout').value,
            MarkDefault: view.querySelector('#mergeDefault').checked,
            UseCloudApi: useCloudApi,
            DoubleSubApiKey: doublesubApiKey,
            Background: true
        })
        .then(function (data) {
            if (data.Running && data.JobId) {
                mergeJob = data.JobId;
                mergeStep = data.Step || '';
                pollMerge(view);
            } else {
                finishMerge(view, data);
            }
        })
        .catch(function (e) {
            console.error('[SubMerger] Merge error:', e);
            finishMerge(view, { Success: false, Error: 'Erreur: ' + e.message });
        });
    }

    // ---------- Traduction (API Claude) ----------

    function setTranslateRunning(view, running) {
        var btnTranslate = view.querySelector('#btnTranslate');
        var btnCancel = view.querySelector('#btnCancelTranslate');
        var progress = view.querySelector('#translateProgress');

        if (btnTranslate) btnTranslate.disabled = running;
        if (btnCancel) btnCancel.style.display = running ? 'inline-block' : 'none';
        if (progress) progress.style.display = running ? 'block' : 'none';
    }

    function updateTranslateProgress(view, data) {
        var bar = view.querySelector('#translateProgressBar');
        var label = view.querySelector('#translateProgressLabel');

        if (bar) bar.style.width = (data.PercentComplete || 0) + '%';
        if (label) {
            var text = data.Message || 'Traduction en cours...';
            if (data.Total > 0) {
                text += ' (' + (data.PercentComplete || 0) + '%)';
            }
            label.textContent = text;
        }
    }

    // Le job n'est plus suivi (termine, perdu ou abandonne) : la page redevient utilisable
    function stopFollowingTranslation(view) {
        currentTranslationJob = null;
        currentTranslationItemId = null;
        translationPollFailures = 0;
        setTranslateRunning(view, false);
    }

    // Reserve de la piste choisie pour la traduction, affichee sous la liste
    function checkTranslateSourceWarning(view) {
        var select = view.querySelector('#translateSource');
        var box = view.querySelector('#translateSourceWarning');
        var option = select.options[select.selectedIndex];

        var message = '';
        if (option && option.value) {
            message = option.dataset.isText === 'false'
                ? 'sous-titre image : il ne contient pas de texte, il ne peut pas etre traduit.'
                : (option.dataset.warning || '');
        }

        box.textContent = message;
        box.style.display = message ? 'block' : 'none';
    }

    // Le champ de la cle et le texte d'aide suivent le moteur choisi
    function showTranslateEngine(view) {
        var claude = view.querySelector('#translateEngine').value === 'claude';
        view.querySelector('#anthropicKeyGroup').style.display = claude ? '' : 'none';
        view.querySelector('#engineHelpClaude').style.display = claude ? 'block' : 'none';
        view.querySelector('#engineHelpGoogle').style.display = claude ? 'none' : 'block';
    }

    function applySettings(view, data) {
        if (!data || !data.Success) return;

        serverHasAnthropicKey = !!data.HasAnthropicKey;
        view.querySelector('#anthropicApiKey').placeholder = serverHasAnthropicKey
            ? 'Cle enregistree (laisser vide pour la garder)'
            : 'sk-ant-...';
    }

    function saveSettings(view, settings) {
        return postPlugin('Settings', settings)
            .then(function (data) {
                applySettings(view, data);
                if (settings.AnthropicApiKey && data && data.Success) view.querySelector('#anthropicApiKey').value = '';
                return data;
            })
            .catch(function (e) {
                console.error('[SubMerger] Settings save error:', e);
            });
    }

    // Reprend le moteur, la langue cible et la presence d'une cle depuis le serveur
    function loadSettings(view) {
        getPlugin('Settings')
            .then(function (data) {
                if (!data || !data.Success) return;

                applySettings(view, data);
                if (data.TranslationEngine) view.querySelector('#translateEngine').value = data.TranslationEngine;
                if (data.DefaultTargetLanguage) view.querySelector('#targetLanguage').value = data.DefaultTargetLanguage;
                showTranslateEngine(view);

                // Une cle gardee par une ancienne version dans ce navigateur est confiee au serveur
                var legacyKey = null;
                try { legacyKey = localStorage.getItem('anthropic_api_key'); } catch (e) { }
                if (legacyKey && !serverHasAnthropicKey) {
                    saveSettings(view, { AnthropicApiKey: legacyKey }).then(function (saved) {
                        if (saved && saved.HasAnthropicKey) localStorage.removeItem('anthropic_api_key');
                    });
                }
            })
            .catch(function (e) {
                console.error('[SubMerger] Settings load error:', e);
            });
    }

    function doTranslate(view) {
        if (!currentMovie) {
            showMessage(view, '#translateResult', 'warning', 'Choisissez d\'abord un film ou un episode.');
            return;
        }

        var sourceEl = view.querySelector('#translateSource');
        var sourceValue = sourceEl.value;
        if (!sourceValue) {
            showMessage(view, '#translateResult', 'warning', 'Choisissez le sous-titre a traduire.');
            return;
        }

        var selected = sourceEl.options[sourceEl.selectedIndex];
        if (selected.dataset.isText === 'false') {
            showMessage(view, '#translateResult', 'warning', 'La traduction ne fonctionne que sur les sous-titres texte, pas sur les sous-titres image.');
            return;
        }


        // Le moteur, la langue et la cle sont enregistres sur le serveur : ils suivent d'un navigateur a l'autre.
        // Un champ de cle vide garde la cle deja enregistree.
        var engine = view.querySelector('#translateEngine').value;
        var anthropicKey = view.querySelector('#anthropicApiKey').value.trim();
        var settings = { TranslationEngine: engine, DefaultTargetLanguage: view.querySelector('#targetLanguage').value };
        if (anthropicKey) settings.AnthropicApiKey = anthropicKey;
        saveSettings(view, settings);

        if (engine === 'claude' && !anthropicKey && !serverHasAnthropicKey) {
            showMessage(view, '#translateResult', 'warning', 'Le moteur Claude demande une cle API Anthropic. Sans cle, choisissez Google Traduction (gratuit).');
            return;
        }

        var itemId = currentMovie.Id;

        view.querySelector('#translateResult').innerHTML = '';
        setTranslateRunning(view, true);

        postPlugin('Translate', {
            VideoPath: currentVideoPath(),
            SourceIndex: parseInt(sourceValue),
            SourceIsExternal: selected.dataset.isExternal === 'true',
            SourcePath: selected.dataset.path || '',
            SourceLanguage: selected.dataset.lang || '',
            TargetLanguage: view.querySelector('#targetLanguage').value,
            Engine: engine,
            Overwrite: view.querySelector('#translateOverwrite').checked,
            AnthropicApiKey: anthropicKey,
            ThenMerge: view.querySelector('#translateThenMerge').checked,
            MergeMode: view.querySelector('#mergeMode').value,
            ToleranceMs: readTolerance(view),
            Layout: view.querySelector('#layout').value
        })
        .then(function (data) {
            console.log('[SubMerger] Translate response:', data);
            if (!data.Success) {
                setTranslateRunning(view, false);
                showMessage(view, '#translateResult', 'error', data.Error);
                return;
            }
            currentTranslationJob = data.JobId;
            currentTranslationItemId = itemId;
            translationPollFailures = 0;
            pollTranslation(view);
        })
        .catch(function (e) {
            setTranslateRunning(view, false);
            console.error('[SubMerger] Translate error:', e);
            showMessage(view, '#translateResult', 'error', 'Erreur: ' + e.message);
        });
    }

    function schedulePoll(view, delayMs) {
        clearTimeout(translationPollTimer);
        // Page quittee : le suivi reprend au retour (viewshow)
        if (!pageVisible) return;

        translationPollTimer = setTimeout(function () {
            pollTranslation(view);
        }, delayMs);
    }

    function pollTranslation(view) {
        var jobId = currentTranslationJob;
        if (!jobId) return;

        getPlugin('TranslateStatus', 'JobId=' + encodeURIComponent(jobId))
            .then(function (data) {
                if (jobId !== currentTranslationJob) return;
                translationPollFailures = 0;

                if (!data.Found) {
                    stopFollowingTranslation(view);
                    showMessage(view, '#translateResult', 'error', 'Tache de traduction introuvable ou expiree.');
                    return;
                }

                updateTranslateProgress(view, data);

                if (data.Status === 'Running' || data.Status === 'Pending') {
                    // Le job est long : on interroge toutes les 3 secondes
                    schedulePoll(view, 3000);
                    return;
                }

                var itemId = currentTranslationItemId;
                stopFollowingTranslation(view);

                if (data.Status === 'Completed') {
                    var html = '<strong>' + escapeHtml(data.Message) + '</strong><br><small>'
                        + escapeHtml(data.MergedPath || data.OutputPath) + '</small>';
                    if (data.Untranslated > 0) {
                        html += '<br><span style="color:#ff9800;">' + data.Untranslated + ' replique(s) non traduite(s), texte d\'origine conserve</span>';
                    }
                    if (data.InputTokens > 0) {
                        html += '<br><small style="color:#888;">' + data.InputTokens + ' tokens entree / ' + data.OutputTokens + ' sortie</small>';
                    }

                    showProducedThenRefresh(view, '#translateResult', itemId, html);
                } else if (data.Status === 'Cancelled') {
                    showMessage(view, '#translateResult', 'warning', 'Traduction annulee.');
                } else {
                    showMessage(view, '#translateResult', 'error', data.Error || 'Erreur inconnue');
                }
            })
            .catch(function (e) {
                console.error('[SubMerger] Poll error:', e);
                if (jobId !== currentTranslationJob) return;

                // Une erreur reseau ponctuelle ne doit pas tuer le suivi, mais on ne retente pas sans fin
                translationPollFailures++;
                if (translationPollFailures >= 10) {
                    stopFollowingTranslation(view);
                    showMessage(view, '#translateResult', 'error', 'Suivi interrompu : le serveur ne repond plus. La traduction continue peut-etre cote serveur.');
                    return;
                }
                schedulePoll(view, 5000);
            });
    }

    function cancelTranslation(view) {
        if (!currentTranslationJob) return;

        view.querySelector('#translateProgressLabel').textContent = 'Annulation demandee...';

        postPlugin('TranslateCancel', { JobId: currentTranslationJob }).catch(function (e) {
            console.error('[SubMerger] Cancel error:', e);
        });
    }

    // ---------- Renommage des anciens .dual.srt ----------

    function renameLegacyDual(view, dryRun) {
        var box = view.querySelector('#renameResult');
        var btnPreview = view.querySelector('#btnRenamePreview');
        var btnApply = view.querySelector('#btnRenameApply');

        btnPreview.disabled = true;
        btnApply.disabled = true;
        box.innerHTML = statusBox('info', dryRun ? 'Analyse des mediatheques...' : 'Renommage en cours...');

        postPlugin('RenameLegacyDual', { DryRun: dryRun })
        .then(function (data) {
            btnPreview.disabled = false;
            btnApply.disabled = false;

            if (!data.Success) {
                btnApply.style.display = 'none';
                box.innerHTML = statusBox('error', escapeHtml(data.Error));
                return;
            }

            var summary;
            if (data.Found === 0) {
                summary = 'Aucun fichier .dual.srt trouve dans ' + data.Folders.length + ' dossier(s) de mediatheque.';
            } else if (dryRun) {
                summary = data.Found + ' fichier(s) trouve(s) : ' + data.Renamed + ' a renommer, ' + data.Skipped + ' ignore(s).';
            } else {
                summary = data.Renamed + ' fichier(s) renomme(s), ' + data.Skipped + ' ignore(s), ' + data.Errors + ' erreur(s).';
                if (data.Renamed > 0) {
                    summary += ' Analyse de la mediatheque demandee.';
                }
            }

            var rows = data.Items.map(function (item) {
                var color = (item.Status === 'Renamed' || item.Status === 'WouldRename') ? '#4CAF50'
                    : (item.Status === 'Error' ? '#f44336' : '#ff9800');
                var target = item.NewName ? ' &rarr; <strong>' + escapeHtml(item.NewName) + '</strong>' : '';
                // Un fichier que le renommage ne sait pas traiter peut etre mis de cote
                var pick = (item.Status === 'Skipped' && !item.NewName)
                    ? '<input type="checkbox" class="legacyRemove" data-path="' + escapeHtml(item.OldPath).replace(/"/g, '&quot;') + '" style="margin-right:8px;">'
                    : '';
                return '<div style="padding:6px 8px; margin:4px 0; background:rgba(0,0,0,0.3); border-left:3px solid ' + color + '; border-radius:4px; font-size:0.85em;">'
                    + pick + escapeHtml(item.OldPath) + target
                    + '<br><span style="color: inherit; opacity: 0.75;">' + escapeHtml(item.Detail) + '</span></div>';
            }).join('');

            box.innerHTML = statusBox(data.Errors > 0 ? 'warning' : 'ok', '<strong>' + summary + '</strong>') + rows;

            // Le bouton Renommer n'apparait qu'apres un apercu qui a quelque chose a renommer
            btnApply.style.display = (dryRun && data.Renamed > 0) ? 'inline-block' : 'none';
            view.querySelector('#btnRenameRemove').style.display = box.querySelector('.legacyRemove') ? 'inline-block' : 'none';
        })
        .catch(function (e) {
            btnPreview.disabled = false;
            btnApply.disabled = false;
            console.error('[SubMerger] Rename error:', e);
            box.innerHTML = statusBox('error', 'Erreur: ' + escapeHtml(e.message));
        });
    }

    // Met de cote les anciens bilingues coches dans l'apercu du renommage
    function removeLegacyDual(view) {
        var box = view.querySelector('#renameResult');
        var paths = Array.prototype.map.call(box.querySelectorAll('.legacyRemove:checked'), function (input) {
            return input.dataset.path;
        });

        if (paths.length === 0) {
            box.insertAdjacentHTML('afterbegin', statusBox('warning', 'Cochez d\'abord les fichiers a mettre de cote.'));
            return;
        }

        postPlugin('RemoveLegacyDual', { Paths: paths })
        .then(function (data) {
            if (!data.Success) {
                box.innerHTML = statusBox('error', escapeHtml(data.Error));
                return;
            }

            var problems = data.Problems.map(function (item) {
                return '<br>' + escapeHtml(item.OldPath) + ' : ' + escapeHtml(item.Detail);
            }).join('');

            view.querySelector('#btnRenameRemove').style.display = 'none';
            box.innerHTML = statusBox(data.Problems.length > 0 ? 'warning' : 'ok',
                '<strong>' + data.Moved + ' fichier(s) deplace(s) dans le dossier _anciens_dual de leur mediatheque.</strong>'
                + (data.Moved > 0 ? ' Analyse de la mediatheque demandee.' : '') + problems);
        })
        .catch(function (e) {
            console.error('[SubMerger] Remove legacy error:', e);
            box.innerHTML = statusBox('error', 'Erreur: ' + escapeHtml(e.message));
        });
    }

    // ---------- Normalisation (ASS/SSA vers SRT) ----------

    // Signale les programmes manquants sur le serveur. L'avertissement disparait
    // de lui-meme une fois le programme installe : la verification est refaite a chaque affichage.
    function checkNormalizeTools(view) {
        var box = view.querySelector('#normalizeTools');
        var tagMkv = view.querySelector('#normalizeTagMkv');

        getPlugin('Tools')
            .then(function (data) {
                var messages = [];
                var blocking = !data.FfmpegAvailable || !data.FfprobeAvailable;

                if (blocking) {
                    messages.push('<strong>ffmpeg ou ffprobe est introuvable sur le serveur.</strong> La normalisation ne peut pas fonctionner sans eux.');
                }
                if (!data.MkvPropEditAvailable) {
                    messages.push('<strong>MKVToolNix n\'est pas installe sur le serveur.</strong> Tout fonctionne sauf la modification du titre des pistes integrees aux .mkv (ajout ou retrait de la note). '
                        + 'Sur un Synology : Centre de paquets, source <em>https://packages.synocommunity.com</em>, paquet MKVToolNix. Ce message disparaitra une fois le paquet installe.');
                }

                tagMkv.disabled = !data.MkvPropEditAvailable;
                if (!data.MkvPropEditAvailable) tagMkv.checked = false;
                normalizeBlocked = blocking;
                refreshNormalizeButtons(view);

                box.className = 'status-box ' + (blocking ? 'status-error' : 'status-warning');
                box.innerHTML = messages.join('<br><br>');
                box.style.display = messages.length ? 'block' : 'none';
            })
            .catch(function (e) {
                console.error('[SubMerger] Tools check error:', e);
            });
    }

    // Le nettoyage existe a deux endroits : onglet Normaliser (film choisi ou tout) et onglet Maintenance
    // (toutes les mediatheques). Chacun a ses cases, sa barre et sa zone de resultat.
    var NORMALIZE_UI = {
        tab: {
            progress: '#normalizeProgress', bar: '#normalizeProgressBar', label: '#normalizeProgressLabel',
            result: '#normalizeResult', cleanupApply: '#btnCleanupApply',
            conversions: '#cleanupConversions', bilingual: '#cleanupBilingual', note: '#cleanupNote'
        },
        library: {
            progress: '#libProgress', bar: '#libProgressBar', label: '#libProgressLabel',
            result: '#libResult', cleanupApply: '#btnLibCleanupApply',
            conversions: '#libCleanupConversions', bilingual: '#libCleanupBilingual', note: '#libCleanupNote'
        },
        batch: {
            progress: '#batchProgress', bar: '#batchProgressBar', label: '#batchProgressLabel',
            result: '#batchResult'
        }
    };

    function normalizeUi(view, name) {
        return view.querySelector(NORMALIZE_UI[normalizeTarget][name]);
    }

    var NORMALIZE_APPLY_BUTTONS = ['#btnNormalizeApply', '#btnCleanupApply', '#btnLibCleanupApply', '#btnBatchApply'];

    // Les boutons sont grises pendant une tache, et tant que ffmpeg ou ffprobe manque
    function refreshNormalizeButtons(view) {
        ['#btnNormalizePreview', '#btnCleanupPreview', '#btnLibCleanupPreview', '#btnBatchPreview'].concat(NORMALIZE_APPLY_BUTTONS).forEach(function (selector) {
            view.querySelector(selector).disabled = normalizeBusy || normalizeBlocked;
        });
    }

    // Un reglage a change depuis l'apercu : ce qu'il montre n'est plus ce qui serait execute
    function invalidateNormalizePreview(view) {
        normalizePreview = null;
        NORMALIZE_APPLY_BUTTONS.forEach(function (selector) {
            view.querySelector(selector).style.display = 'none';
        });
    }

    // La normalisation et le nettoyage partagent la meme tache de fond : une seule a la fois
    function setNormalizeRunning(view, running) {
        normalizeBusy = running;
        refreshNormalizeButtons(view);
        view.querySelector('#normalizeProgress').style.display = 'none';
        view.querySelector('#libProgress').style.display = 'none';
        view.querySelector('#batchProgress').style.display = 'none';
        normalizeUi(view, 'progress').style.display = running ? 'block' : 'none';
    }

    function stopFollowingNormalize(view) {
        normalizeJob = null;
        normalizePollFailures = 0;
        setNormalizeRunning(view, false);
    }

    // cleanup = true : retire les .srt generes au lieu d'en creer
    // fromLibrary = true : nettoyage de l'onglet Maintenance, toujours sur toutes les mediatheques
    function startNormalize(view, dryRun, cleanup, target) {
        var preview = normalizePreview;
        var body;

        if (dryRun) {
            normalizeTarget = target || 'tab';

            var batch = normalizeTarget === 'batch';
            var wholeLibrary = normalizeTarget === 'library'
                || view.querySelector(batch ? '#batchScope' : '#normalizeScope').value === 'all';
            if (!wholeLibrary && !currentMovie) {
                normalizeUi(view, 'result').innerHTML = statusBox('warning', 'Selectionnez d\'abord un film ou un episode, ou choisissez "Toutes les mediatheques".');
                return;
            }

            if (batch) {
                // La disposition et les options sont celles de l'onglet Fusionner
                body = {
                    DryRun: true,
                    BatchMerge: true,
                    Language1: view.querySelector('#batchLang1').value,
                    Language2: view.querySelector('#batchLang2').value,
                    MergeMode: view.querySelector('#mergeMode').value,
                    ToleranceMs: readTolerance(view),
                    Layout: view.querySelector('#layout').value,
                    MarkDefault: view.querySelector('#mergeDefault').checked,
                    VideoPath: wholeLibrary ? '' : currentVideoPath()
                };
            } else {
                body = {
                    DryRun: true,
                    Cleanup: !!cleanup,
                    CleanupConversions: normalizeUi(view, 'conversions').checked,
                    CleanupBilingual: normalizeUi(view, 'bilingual').checked,
                    CleanupNote: normalizeUi(view, 'note').checked,
                    VideoPath: wholeLibrary ? '' : currentVideoPath(),
                    Languages: view.querySelector('#normalizeLanguages').value,
                    Overwrite: view.querySelector('#normalizeOverwrite').checked,
                    ExtractText: view.querySelector('#normalizeExtract').checked,
                    Annotate: view.querySelector('#normalizeAnnotate').checked,
                    TagMkvTracks: view.querySelector('#normalizeTagMkv').checked
                };
            }
            preview = { body: body, itemId: wholeLibrary ? null : currentMovie.Id, target: normalizeTarget };
        } else {
            // "Appliquer" rejoue la demande de l'apercu telle quelle : on execute ce qui a ete montre,
            // meme si le film, le perimetre ou une case a change depuis
            if (!preview) return;
            normalizeTarget = preview.target;
            body = Object.assign({}, preview.body, { DryRun: false });
        }

        var box = normalizeUi(view, 'result');
        normalizeItemId = preview.itemId;
        normalizeJoined = false;

        invalidateNormalizePreview(view);
        if (dryRun) normalizePreview = preview;

        setNormalizeRunning(view, true);
        normalizeUi(view, 'bar').style.width = '0%';
        normalizeUi(view, 'label').textContent = 'Preparation...';
        box.innerHTML = '';

        postPlugin('Normalize', body)
        .then(function (data) {
            if (!data.Success) {
                setNormalizeRunning(view, false);
                box.innerHTML = statusBox('error', escapeHtml(data.Error));
                return;
            }
            normalizeJob = data.JobId;
            normalizeJoined = !!data.AlreadyRunning;
            if (normalizeJoined) normalizePreview = null;
            normalizePollFailures = 0;
            pollNormalize(view);
        })
        .catch(function (e) {
            setNormalizeRunning(view, false);
            console.error('[SubMerger] Normalize error:', e);
            box.innerHTML = statusBox('error', 'Erreur: ' + escapeHtml(e.message));
        });
    }

    function scheduleNormalizePoll(view, delayMs) {
        clearTimeout(normalizePollTimer);
        if (!pageVisible) return;

        normalizePollTimer = setTimeout(function () {
            pollNormalize(view);
        }, delayMs);
    }

    function pollNormalize(view) {
        var jobId = normalizeJob;
        if (!jobId) return;

        var box = normalizeUi(view, 'result');

        getPlugin('NormalizeStatus', 'JobId=' + encodeURIComponent(jobId))
            .then(function (data) {
                if (jobId !== normalizeJob) return;
                normalizePollFailures = 0;

                if (!data.Found) {
                    stopFollowingNormalize(view);
                    box.innerHTML = statusBox('error', 'Tache introuvable ou expiree.');
                    return;
                }

                normalizeUi(view, 'bar').style.width = (data.PercentComplete || 0) + '%';
                normalizeUi(view, 'label').textContent =
                    (data.Message || 'Analyse...') + (data.Total > 0 ? ' (' + data.Processed + '/' + data.Total + ')' : '');

                if (data.Status === 'Running' || data.Status === 'Pending') {
                    scheduleNormalizePoll(view, 2000);
                    return;
                }

                stopFollowingNormalize(view);
                showNormalizeResult(view, data);
            })
            .catch(function (e) {
                console.error('[SubMerger] Normalize poll error:', e);
                if (jobId !== normalizeJob) return;

                normalizePollFailures++;
                if (normalizePollFailures >= 10) {
                    stopFollowingNormalize(view);
                    box.innerHTML = statusBox('error', 'Suivi interrompu : le serveur ne repond plus. La tache continue peut-etre cote serveur.');
                    return;
                }
                scheduleNormalizePoll(view, 5000);
            });
    }

    function showNormalizeResult(view, data) {
        var box = normalizeUi(view, 'result');
        var btnApply = data.BatchMerge ? view.querySelector('#btnBatchApply')
            : (data.Cleanup ? normalizeUi(view, 'cleanupApply') : view.querySelector('#btnNormalizeApply'));

        if (data.Status === 'Cancelled') {
            box.innerHTML = statusBox('warning', data.BatchMerge ? 'Fusion en serie annulee.' : (data.Cleanup ? 'Nettoyage annule.' : 'Normalisation annulee.'));
            return;
        }
        if (data.Status !== 'Completed') {
            box.innerHTML = statusBox('error', escapeHtml(data.Error || 'Erreur inconnue'));
            return;
        }

        var changes = data.Created + data.Renamed + data.Tagged + data.Deleted + (data.Merged || 0);
        var summary;
        if (data.BatchMerge) {
            var without = data.Unmatched > 0 ? ' ' + data.Unmatched + ' video(s) n\'ont pas les deux langues.' : '';
            if (changes === 0 && data.Failures === 0) {
                summary = 'Rien a creer sur ' + data.Total + ' video(s) : les bilingues existent deja, ou les deux langues manquent.' + without;
            } else if (data.DryRun) {
                summary = 'Apercu : ' + data.Merged + ' bilingue(s) a creer sur ' + data.Total + ' video(s).' + without;
            } else {
                summary = data.Merged + ' bilingue(s) cree(s), ' + data.Failures + ' echec(s).' + without;
                if (changes > 0) summary += ' Analyse de la mediatheque demandee.';
            }
        } else if (data.Cleanup) {
            var notes = data.Renamed + data.Tagged;
            if (changes === 0 && data.Failures === 0) {
                summary = 'Rien a nettoyer (' + data.Total + ' video(s) examinees).';
            } else if (data.DryRun) {
                summary = 'Apercu : ' + data.Deleted + ' fichier(s) a supprimer'
                    + (notes > 0 ? ', ' + data.Renamed + ' fichier(s) a renommer et ' + data.Tagged + ' titre(s) de piste a raccourcir' : '') + '.';
            } else {
                summary = data.Deleted + ' fichier(s) supprime(s), '
                    + (notes > 0 ? data.Renamed + ' fichier(s) renomme(s), ' + data.Tagged + ' titre(s) de piste raccourci(s), ' : '')
                    + data.Failures + ' echec(s).';
                if (changes > 0) summary += ' Analyse de la mediatheque demandee.';
            }
        } else if (changes === 0 && data.Failures === 0) {
            summary = 'Rien a faire : aucun sous-titre a convertir, renommer ou annoter (' + data.Total + ' fichier(s) examines).';
        } else if (data.DryRun) {
            summary = 'Apercu : ' + data.Created + ' .srt a creer, ' + data.Renamed + ' fichier(s) a renommer, ' + data.Tagged + ' piste(s) a annoter.';
        } else {
            summary = data.Created + ' .srt cree(s), ' + data.Renamed + ' fichier(s) renomme(s), ' + data.Tagged + ' piste(s) annotee(s), ' + data.Failures + ' echec(s).';
            if (changes > 0) summary += ' Analyse de la mediatheque demandee.';
        }
        if (data.ImageTracks > 0) {
            summary += ' ' + data.ImageTracks + ' sous-titre(s) image (PGS/VOBSUB) non convertis.';
        }

        var rows = data.Actions.map(function (action) {
            var color = action.Status === 'Failed' ? '#f44336'
                : ((action.Kind === 'Info' || action.Kind === 'Delete') ? '#ff9800' : '#4CAF50');
            var detail = action.Detail || '';
            if (action.Kind === 'Delete' && action.Status !== 'Failed') {
                detail = (data.DryRun ? 'A supprimer : ' : 'Supprime : ') + detail;
            }
            var target = action.Target ? ' &rarr; <strong>' + escapeHtml(action.Target) + '</strong>' : '';
            return '<div style="padding:6px 8px; margin:4px 0; background:rgba(0,0,0,0.3); border-left:3px solid ' + color + '; border-radius:4px; font-size:0.85em;">'
                + escapeHtml(String(action.Source || '').split(/[\\/]/).pop()) + target
                + '<br><span style="color: inherit; opacity: 0.75;">' + escapeHtml(detail) + '</span></div>';
        }).join('');

        var listed = data.ActionsTruncated
            ? '<p style="color: inherit; opacity: 0.75; font-size:0.85em;">Seules les ' + data.Actions.length + ' premieres lignes sont affichees ; les totaux ci-dessus sont complets.</p>'
            : '';

        var joined = normalizeJoined
            ? statusBox('warning', 'Une autre normalisation ou un autre nettoyage etait deja en cours : voici son resultat. Relancez votre demande.')
            : '';

        box.innerHTML = joined + statusBox(data.Failures > 0 ? 'warning' : 'ok', '<strong>' + summary + '</strong>') + rows + listed;

        // Le bouton d'execution n'apparait qu'apres un apercu qui a quelque chose a faire,
        // et seulement si cet apercu est bien celui de la demande memorisee
        btnApply.style.display = (data.DryRun && changes > 0 && normalizePreview && !normalizeJoined) ? 'inline-block' : 'none';

        // Apres une vraie operation sur un seul film, ses pistes sont rechargees
        if (!data.DryRun && changes > 0 && normalizeItemId) {
            var itemId = normalizeItemId;
            refreshItemMetadata(view, itemId).then(function () {
                if (currentMovie && currentMovie.Id === itemId) {
                    reloadCurrentMovie(view);
                }
            });
        }
    }

    function cancelNormalize() {
        if (!normalizeJob) return;

        postPlugin('NormalizeCancel', { JobId: normalizeJob }).catch(function (e) {
            console.error('[SubMerger] Normalize cancel error:', e);
        });
    }

    // Branche les boutons et les listes. Appele une seule fois : viewshow peut se
    // redeclencher sur la meme page, et un bouton branche deux fois lancerait deux traductions.
    function bindEvents(view) {
        function on(selector, eventName, handler) {
            var element = view.querySelector(selector);
            if (element) element.addEventListener(eventName, handler);
        }

        on('#btnLoad', 'click', function () { loadMovies(view); });

        on('#moviesSelect', 'change', function () { onMovieChange(view); });
        // Double-click pour selectionner
        on('#moviesSelect', 'dblclick', function () { onMovieChange(view); });

        // Recherche en temps reel
        var searchTimeout;
        on('#searchFilter', 'input', function () {
            clearTimeout(searchTimeout);
            searchTimeout = setTimeout(function() {
                filterMovies(view);
            }, 300);
        });

        // Recharger quand on change de filtre
        on('#typeFilter', 'change', function () { if (allMovies.length > 0) loadMovies(view); });
        on('#libraryFilter', 'change', function () { if (allMovies.length > 0) loadMovies(view); });

        on('#btnRefresh', 'click', function () { refreshMovieMetadata(view); });
        on('#btnMerge', 'click', function () { doMerge(view); });
        on('#modeLocal', 'click', function () { selectMode(view, 'local'); });
        on('#modeCloud', 'click', function () { selectMode(view, 'cloud'); });

        on('#btnTranslate', 'click', function () { doTranslate(view); });
        on('#btnCancelTranslate', 'click', function () { cancelTranslation(view); });

        on('#btnRenamePreview', 'click', function () { renameLegacyDual(view, true); });
        on('#btnRenameApply', 'click', function () { renameLegacyDual(view, false); });

        on('#btnNormalizePreview', 'click', function () { startNormalize(view, true, false); });
        on('#btnNormalizeApply', 'click', function () { startNormalize(view, false, false); });
        on('#btnCleanupPreview', 'click', function () { startNormalize(view, true, true); });
        on('#btnCleanupApply', 'click', function () { startNormalize(view, false, true); });
        on('#btnNormalizeCancel', 'click', function () { cancelNormalize(); });
        on('#btnLibCleanupPreview', 'click', function () { startNormalize(view, true, true, 'library'); });
        on('#btnLibCleanupApply', 'click', function () { startNormalize(view, false, true, 'library'); });
        on('#btnBatchPreview', 'click', function () { startNormalize(view, true, false, 'batch'); });
        on('#btnBatchApply', 'click', function () { startNormalize(view, false, false, 'batch'); });
        on('#btnBatchCancel', 'click', function () { cancelNormalize(); });

        // Les langues de la fusion en serie sont celles proposees pour la traduction
        var languageOptions = view.querySelector('#targetLanguage').innerHTML;
        view.querySelector('#batchLang1').innerHTML = languageOptions;
        view.querySelector('#batchLang2').innerHTML = languageOptions;
        view.querySelector('#batchLang1').value = 'en';
        view.querySelector('#batchLang2').value = 'fr';
        on('#btnLibCancel', 'click', function () { cancelNormalize(); });
        on('#btnMergeCancel', 'click', function () { cancelMerge(); });
        on('#btnRenameRemove', 'click', function () { removeLegacyDual(view); });
        on('#translateEngine', 'change', function () { showTranslateEngine(view); });

        ['#normalizeScope', '#normalizeLanguages', '#normalizeAnnotate', '#normalizeTagMkv', '#normalizeExtract', '#normalizeOverwrite',
            '#cleanupConversions', '#cleanupBilingual', '#cleanupNote',
            '#libCleanupConversions', '#libCleanupBilingual', '#libCleanupNote',
            '#batchLang1', '#batchLang2', '#batchScope', '#layout', '#mergeMode', '#mergeDefault',
            '#moviesSelect', '#versionSelect'].forEach(function (selector) {
            on(selector, 'change', function () { invalidateNormalizePreview(view); });
            on(selector, 'input', function () { invalidateNormalizePreview(view); });
        });

        var tabs = view.querySelectorAll('.merger-tab');
        for (var t = 0; t < tabs.length; t++) {
            tabs[t].addEventListener('click', function () { selectTab(view, this.dataset.tab); });
        }

        // Dernier onglet utilise
        var savedTab = null;
        try { savedTab = localStorage.getItem('submerger_tab'); } catch (e) { }
        if (savedTab && view.querySelector('.tab-panel[data-panel="' + savedTab + '"]')) {
            selectTab(view, savedTab);
        }
    }

    return function (view) {
        console.log('[SubMerger] Controller init v9.12.0');

        var bound = false;

        view.addEventListener('viewshow', function () {
            console.log('[SubMerger] viewshow event');
            pageVisible = true;

            // Charger les mediatheques
            loadLibraries(view);

            if (!bound) {
                bound = true;
                bindEvents(view);
            }

            showTranslateEngine(view);
            loadSettings(view);

            checkNormalizeTools(view);
            if (normalizeJob) {
                // Une normalisation tournait quand la page a ete quittee : on reprend son suivi
                setNormalizeRunning(view, true);
                pollNormalize(view);
            }

            if (mergeJob) {
                // Une fusion tournait quand la page a ete quittee : on reprend son suivi
                setMergeRunning(view, true);
                pollMerge(view);
            }

            if (currentTranslationJob) {
                // Une traduction tournait quand la page a ete quittee : on reprend son suivi
                setTranslateRunning(view, true);
                pollTranslation(view);
            } else {
                if (!currentMovie) {
                    log(view, 'Choisissez une mediatheque puis cliquez sur "Charger la liste".');
                }
            }
        });

        // Quitter la page suspend le suivi ; les taches continuent cote serveur
        view.addEventListener('viewhide', function () {
            pageVisible = false;
            clearInterval(mergeTimer);
            clearTimeout(translationPollTimer);
            translationPollTimer = null;
            clearTimeout(normalizePollTimer);
            normalizePollTimer = null;
            clearTimeout(mergePollTimer);
            mergePollTimer = null;
        });
    };
});
