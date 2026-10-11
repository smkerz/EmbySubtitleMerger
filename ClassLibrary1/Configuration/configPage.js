define([], function () {
    'use strict';

    console.log('[SubMerger] Module loaded v8.9.0');

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
    var pageVisible = false;             // La page du plugin est affichee

    // Sections qui dependent du film et de la version selectionnes
    var WORK_SECTIONS = ['#subtitlesSection', '#optionsSection', '#mergeModeSection', '#mergeSection', '#translateSection'];

    function getApiClient() {
        return window.ApiClient;
    }

    // URL d'un endpoint du plugin, authentifiee avec le jeton de la session Emby
    function pluginUrl(name, query) {
        var apiClient = getApiClient();
        var url = apiClient.serverAddress() + '/EmbySubtitleMerger/' + name + '?api_key=' + apiClient.accessToken();
        return query ? url + '&' + query : url;
    }

    function getPlugin(name, query) {
        return fetch(pluginUrl(name, query)).then(function (r) { return r.json(); });
    }

    function postPlugin(name, body) {
        return fetch(pluginUrl(name), {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        }).then(function (r) { return r.json(); });
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
    }

    function log(view, msg) {
        console.log('[SubMerger] ' + msg);
        var info = view.querySelector('#movieInfo');
        if (info) {
            info.textContent = msg;
            info.className = 'status-box status-warning';
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
                var label = '#' + s.Index + ' ';
                label += s.Language ? s.Language.toUpperCase() : 'UND';
                label += ' (' + (s.Codec || '?') + ')';
                label += isExternal ? ' [EXT]' : ' [INT]';
                if (!isText) label += ' [IMAGE]';
                // Ajouter le nom du fichier pour les sous-titres externes
                if (isExternal && subPath) {
                    var fileName = subPath.split(/[\\/]/).pop();
                    if (fileName) label += ' - ' + fileName;
                }
                subs.push({ index: s.Index, label: label, isText: isText, isExternal: isExternal, path: subPath, lang: s.Language || '' });
            }
        }

        var s1 = view.querySelector('#sub1');
        var s2 = view.querySelector('#sub2');
        s1.innerHTML = '<option value="">-- Choisir --</option>';
        s2.innerHTML = '<option value="">-- Choisir --</option>';

        function makeOption(sub) {
            var option = document.createElement('option');
            option.value = sub.index;
            option.textContent = sub.label;
            option.dataset.isText = sub.isText;
            option.dataset.isExternal = sub.isExternal;
            option.dataset.path = sub.path;
            option.dataset.lang = sub.lang;
            return option;
        }

        for (var j = 0; j < subs.length; j++) {
            s1.appendChild(makeOption(subs[j]));
            s2.appendChild(makeOption(subs[j]));
        }

        s1.onchange = function() { checkImageWarning(view); };
        s2.onchange = function() { checkImageWarning(view); };

        setWorkSectionsVisible(view, true);

        success(view, subs.length + ' sous-titres trouves');

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

    function checkImageWarning(view) {
        var s1 = view.querySelector('#sub1');
        var s2 = view.querySelector('#sub2');
        var warning = view.querySelector('#imageWarning');

        var sel1 = s1.options[s1.selectedIndex];
        var sel2 = s2.options[s2.selectedIndex];

        var hasImage = false;
        if (sel1 && sel1.dataset.isText === 'false') hasImage = true;
        if (sel2 && sel2.dataset.isText === 'false') hasImage = true;

        if (warning) {
            warning.style.display = hasImage ? 'block' : 'none';
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

    // Affiche le fichier produit, demande a Emby de le prendre en compte, puis recharge les pistes
    function showProducedThenRefresh(view, itemId, html) {
        var result = view.querySelector('#result');
        result.innerHTML = statusBox('ok', html + '<br><em>Actualisation des metadonnees...</em>');

        refreshItemMetadata(view, itemId).then(function() {
            result.innerHTML = statusBox('ok', html + '<br><em>Actualisation demandee : le sous-titre devrait apparaitre dans Emby.</em>');
            // Ne pas ecraser la selection si l'utilisateur est passe a un autre film entre-temps
            if (currentMovie && currentMovie.Id === itemId) {
                reloadCurrentMovie(view);
            }
        });
    }

    function doMerge(view) {
        var sub1El = view.querySelector('#sub1');
        var sub2El = view.querySelector('#sub2');
        var v1 = sub1El.value;
        var v2 = sub2El.value;
        var result = view.querySelector('#result');

        if (!currentMovie || !v1 || !v2) {
            error(view, 'Selectionnez 2 sous-titres');
            return;
        }
        if (v1 === v2) {
            error(view, 'Choisissez 2 sous-titres differents');
            return;
        }

        var sel1 = sub1El.options[sub1El.selectedIndex];
        var sel2 = sub2El.options[sub2El.selectedIndex];

        var doublesubApiKey = '';
        if (useCloudApi) {
            doublesubApiKey = view.querySelector('#apiKey').value.trim();
            if (!doublesubApiKey) {
                error(view, 'Cle API DoubleSub.io requise pour le mode cloud');
                return;
            }
            if (!doublesubApiKey.startsWith('dsub_')) {
                error(view, 'Cle API invalide (doit commencer par dsub_)');
                return;
            }
            localStorage.setItem('doublesub_api_key', doublesubApiKey);
        }

        var cloud = useCloudApi;
        var itemId = currentMovie.Id;
        log(view, 'Fusion ' + (cloud ? 'DoubleSub.io' : 'locale') + ' en cours...');

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
            UseCloudApi: cloud,
            DoubleSubApiKey: doublesubApiKey
        })
        .then(function (data) {
            console.log('[SubMerger] Merge response:', data);
            if (data.Success) {
                success(view, 'Fusion reussie! ' + data.CueCount + ' sous-titres' + (cloud ? ' (via DoubleSub.io)' : ' (local)'));
                showProducedThenRefresh(view, itemId,
                    '<strong>' + escapeHtml(data.Message) + '</strong><br><small>' + escapeHtml(data.OutputPath) + '</small>');
            } else {
                error(view, 'Erreur: ' + data.Error);
                result.innerHTML = statusBox('error', escapeHtml(data.Error));
            }
        })
        .catch(function (e) {
            console.error('[SubMerger] Merge error:', e);
            error(view, 'Erreur: ' + e.message);
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

    function doTranslate(view) {
        var result = view.querySelector('#result');

        if (!currentMovie) {
            error(view, 'Selectionnez d\'abord un film ou un episode');
            return;
        }

        var sourceEl = view.querySelector('#sub1');
        var sourceValue = sourceEl.value;
        if (!sourceValue) {
            error(view, 'Choisissez la piste a traduire dans "Sous-titre 1"');
            return;
        }

        var selected = sourceEl.options[sourceEl.selectedIndex];
        if (selected.dataset.isText === 'false') {
            error(view, 'La traduction ne fonctionne que sur les sous-titres texte (pas les sous-titres image)');
            return;
        }

        // La cle n'est memorisee que dans ce navigateur ; vider le champ l'oublie
        var anthropicKey = view.querySelector('#anthropicApiKey').value.trim();
        if (anthropicKey) {
            localStorage.setItem('anthropic_api_key', anthropicKey);
        } else {
            localStorage.removeItem('anthropic_api_key');
        }

        var itemId = currentMovie.Id;

        log(view, 'Demarrage de la traduction...');
        setTranslateRunning(view, true);

        postPlugin('Translate', {
            VideoPath: currentVideoPath(),
            SourceIndex: parseInt(sourceValue),
            SourceIsExternal: selected.dataset.isExternal === 'true',
            SourcePath: selected.dataset.path || '',
            SourceLanguage: selected.dataset.lang || '',
            TargetLanguage: view.querySelector('#targetLanguage').value,
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
                error(view, 'Erreur: ' + data.Error);
                result.innerHTML = statusBox('error', escapeHtml(data.Error));
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
            error(view, 'Erreur: ' + e.message);
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

        var result = view.querySelector('#result');

        getPlugin('TranslateStatus', 'JobId=' + encodeURIComponent(jobId))
            .then(function (data) {
                if (jobId !== currentTranslationJob) return;
                translationPollFailures = 0;

                if (!data.Found) {
                    stopFollowingTranslation(view);
                    error(view, 'Job de traduction introuvable ou expire');
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
                    html += '<br><small style="color:#888;">' + data.InputTokens + ' tokens entree / ' + data.OutputTokens + ' sortie</small>';

                    success(view, 'Traduction terminee');
                    showProducedThenRefresh(view, itemId, html);
                } else if (data.Status === 'Cancelled') {
                    log(view, 'Traduction annulee');
                    result.innerHTML = statusBox('warning', 'Traduction annulee.');
                } else {
                    error(view, 'Echec: ' + (data.Error || 'erreur inconnue'));
                    result.innerHTML = statusBox('error', escapeHtml(data.Error || 'Erreur inconnue'));
                }
            })
            .catch(function (e) {
                console.error('[SubMerger] Poll error:', e);
                if (jobId !== currentTranslationJob) return;

                // Une erreur reseau ponctuelle ne doit pas tuer le suivi, mais on ne retente pas sans fin
                translationPollFailures++;
                if (translationPollFailures >= 10) {
                    stopFollowingTranslation(view);
                    error(view, 'Suivi interrompu : le serveur ne repond plus. La traduction continue peut-etre cote serveur.');
                    return;
                }
                schedulePoll(view, 5000);
            });
    }

    function cancelTranslation(view) {
        if (!currentTranslationJob) return;

        log(view, 'Annulation demandee...');

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
                return '<div style="padding:6px 8px; margin:4px 0; background:rgba(0,0,0,0.3); border-left:3px solid ' + color + '; border-radius:4px; font-size:0.85em;">'
                    + escapeHtml(item.OldPath) + target
                    + '<br><span style="color:#9fb0c2;">' + escapeHtml(item.Detail) + '</span></div>';
            }).join('');

            box.innerHTML = statusBox(data.Errors > 0 ? 'warning' : 'ok', '<strong>' + summary + '</strong>') + rows;

            // Le bouton Renommer n'apparait qu'apres un apercu qui a quelque chose a renommer
            btnApply.style.display = (dryRun && data.Renamed > 0) ? 'inline-block' : 'none';
        })
        .catch(function (e) {
            btnPreview.disabled = false;
            btnApply.disabled = false;
            console.error('[SubMerger] Rename error:', e);
            box.innerHTML = statusBox('error', 'Erreur: ' + escapeHtml(e.message));
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
    }

    return function (view) {
        console.log('[SubMerger] Controller init v8.9.0');

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

            var savedAnthropicKey = localStorage.getItem('anthropic_api_key');
            var anthropicInput = view.querySelector('#anthropicApiKey');
            if (savedAnthropicKey && anthropicInput) {
                anthropicInput.value = savedAnthropicKey;
            }

            if (currentTranslationJob) {
                // Une traduction tournait quand la page a ete quittee : on reprend son suivi
                setTranslateRunning(view, true);
                pollTranslation(view);
            } else {
                log(view, 'Pret - Selectionnez les filtres puis cliquez sur Charger');
            }
        });

        // Quitter la page suspend le suivi ; la traduction continue cote serveur
        view.addEventListener('viewhide', function () {
            pageVisible = false;
            clearTimeout(translationPollTimer);
            translationPollTimer = null;
        });
    };
});
