define([], function () {
    'use strict';

    console.log('[SubMerger] Module loaded v8.6');

    var allMovies = [];      // Tous les medias charges
    var filteredMovies = []; // Medias filtres
    var libraries = [];      // Liste des mediatheques
    var currentMovie = null;
    var useCloudApi = false;

    function getApiClient() {
        return window.ApiClient;
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
            Fields: 'Path,MediaStreams,SeriesName,ParentIndexNumber,IndexNumber'
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
        var subSection = view.querySelector('#subtitlesSection');
        var optionsSection = view.querySelector('#optionsSection');
        var mergeModeSection = view.querySelector('#mergeModeSection');
        var mergeSection = view.querySelector('#mergeSection');

        if (isNaN(idx) || idx < 0 || idx >= filteredMovies.length) {
            subSection.style.display = 'none';
            optionsSection.style.display = 'none';
            mergeModeSection.style.display = 'none';
            mergeSection.style.display = 'none';
            return;
        }

        currentMovie = filteredMovies[idx];
        log(view, 'Selection: ' + getItemLabel(currentMovie));

        var subs = [];
        var streams = currentMovie.MediaStreams || [];
        for (var i = 0; i < streams.length; i++) {
            var s = streams[i];
            if (s.Type === 'Subtitle') {
                var isText = s.IsTextSubtitleStream;
                var label = '#' + s.Index + ' ';
                label += s.Language ? s.Language.toUpperCase() : 'UND';
                label += ' (' + (s.Codec || '?') + ')';
                label += s.IsExternal ? ' [EXT]' : ' [INT]';
                if (!isText) label += ' [IMAGE]';
                subs.push({ index: s.Index, label: label, isText: isText });
            }
        }

        var s1 = view.querySelector('#sub1');
        var s2 = view.querySelector('#sub2');
        s1.innerHTML = '<option value="">-- Choisir --</option>';
        s2.innerHTML = '<option value="">-- Choisir --</option>';

        for (var j = 0; j < subs.length; j++) {
            var sub = subs[j];
            var o1 = document.createElement('option');
            o1.value = sub.index;
            o1.textContent = sub.label;
            o1.dataset.isText = sub.isText;
            s1.appendChild(o1);

            var o2 = document.createElement('option');
            o2.value = sub.index;
            o2.textContent = sub.label;
            o2.dataset.isText = sub.isText;
            s2.appendChild(o2);
        }

        s1.onchange = function() { checkImageWarning(view); };
        s2.onchange = function() { checkImageWarning(view); };

        subSection.style.display = 'block';
        optionsSection.style.display = 'block';
        mergeModeSection.style.display = 'block';
        mergeSection.style.display = 'block';
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
            Fields: 'Path,MediaStreams,SeriesName,ParentIndexNumber,IndexNumber'
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

        // Appeler l'API Emby pour rafraichir les metadonnees de l'element
        var refreshUrl = baseUrl + '/emby/Items/' + itemId + '/Refresh?Recursive=false&MetadataRefreshMode=Default&ImageRefreshMode=Default&ReplaceAllMetadata=false&ReplaceAllImages=false&api_key=' + apiKey;

        console.log('[SubMerger] Refreshing item metadata:', itemId);

        return fetch(refreshUrl, { method: 'POST' })
            .then(function(response) {
                if (response.ok) {
                    console.log('[SubMerger] Item metadata refresh triggered');
                    // Attendre un peu pour que Emby traite le refresh
                    return new Promise(function(resolve) {
                        setTimeout(resolve, 2000);
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
        var apiClient = getApiClient();
        var baseUrl = apiClient.serverAddress();
        var apiKey = apiClient.accessToken();
        var statusUrl = baseUrl + '/EmbySubtitleMerger/Status?api_key=' + apiKey;

        var cloudStatus = view.querySelector('#cloudStatus');

        fetch(statusUrl)
            .then(function(r) { return r.json(); })
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

        console.log('[SubMerger] Mode selected: ' + (useCloudApi ? 'DoubleSub.io' : 'Local'));
    }

    function doMerge(view) {
        var v1 = view.querySelector('#sub1').value;
        var v2 = view.querySelector('#sub2').value;
        var result = view.querySelector('#result');

        if (!currentMovie || !v1 || !v2) {
            error(view, 'Selectionnez 2 sous-titres');
            return;
        }
        if (v1 === v2) {
            error(view, 'Choisissez 2 sous-titres differents');
            return;
        }

        var mergeMode = view.querySelector('#mergeMode').value;
        var tolerance = parseInt(view.querySelector('#tolerance').value) || 700;
        var offset1 = parseInt(view.querySelector('#offset1').value) || 0;
        var offset2 = parseInt(view.querySelector('#offset2').value) || 0;

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

        var modeLabel = useCloudApi ? 'DoubleSub.io' : 'locale';
        log(view, 'Fusion ' + modeLabel + ' en cours...');

        var apiClient = getApiClient();
        var embyApiKey = apiClient.accessToken();
        var baseUrl = apiClient.serverAddress();
        var url = baseUrl + '/EmbySubtitleMerger/Merge?api_key=' + embyApiKey;

        console.log('[SubMerger] Merge URL: ' + url);
        console.log('[SubMerger] VideoPath: ' + currentMovie.Path);

        fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                VideoPath: currentMovie.Path,
                PrimaryIndex: parseInt(v1),
                SecondaryIndex: parseInt(v2),
                Mode: mergeMode,
                ToleranceMs: tolerance,
                Offset1Ms: offset1,
                Offset2Ms: offset2,
                UseCloudApi: useCloudApi,
                DoubleSubApiKey: doublesubApiKey
            })
        })
        .then(function (r) { return r.json(); })
        .then(function (data) {
            console.log('[SubMerger] Merge response:', data);
            if (data.Success) {
                var modeUsed = useCloudApi ? ' (via DoubleSub.io)' : ' (local)';
                success(view, 'Fusion reussie! ' + data.CueCount + ' sous-titres' + modeUsed);
                result.innerHTML = '<div class="status-box status-ok"><strong>' + data.Message + '</strong><br><small>' + data.OutputPath + '</small><br><em>Actualisation des metadonnees...</em></div>';

                // Rafraichir les metadonnees de l'element pour voir le nouveau sous-titre
                refreshItemMetadata(view, currentMovie.Id).then(function() {
                    result.innerHTML = '<div class="status-box status-ok"><strong>' + data.Message + '</strong><br><small>' + data.OutputPath + '</small><br><em>Sous-titre visible dans Emby!</em></div>';
                    // Recharger les infos du film pour voir le nouveau sous-titre
                    reloadCurrentMovie(view);
                });
            } else {
                error(view, 'Erreur: ' + data.Error);
                result.innerHTML = '<div class="status-box status-error">' + data.Error + '</div>';
            }
        })
        .catch(function (e) {
            console.error('[SubMerger] Merge error:', e);
            error(view, 'Erreur: ' + e.message);
        });
    }

    return function (view) {
        console.log('[SubMerger] Controller init v8.6');

        view.addEventListener('viewshow', function () {
            console.log('[SubMerger] viewshow event');

            // Charger les mediatheques
            loadLibraries(view);

            var btnLoad = view.querySelector('#btnLoad');
            var moviesSelect = view.querySelector('#moviesSelect');
            var searchFilter = view.querySelector('#searchFilter');
            var typeFilter = view.querySelector('#typeFilter');
            var libraryFilter = view.querySelector('#libraryFilter');
            var btnMerge = view.querySelector('#btnMerge');
            var modeLocal = view.querySelector('#modeLocal');
            var modeCloud = view.querySelector('#modeCloud');

            if (btnLoad) {
                btnLoad.addEventListener('click', function () {
                    loadMovies(view);
                });
            }

            if (moviesSelect) {
                moviesSelect.addEventListener('change', function () {
                    onMovieChange(view);
                });
                // Double-click pour selectionner
                moviesSelect.addEventListener('dblclick', function () {
                    onMovieChange(view);
                });
            }

            // Recherche en temps reel
            if (searchFilter) {
                var searchTimeout;
                searchFilter.addEventListener('input', function () {
                    clearTimeout(searchTimeout);
                    searchTimeout = setTimeout(function() {
                        filterMovies(view);
                    }, 300);
                });
            }

            // Recharger quand on change de filtre
            if (typeFilter) {
                typeFilter.addEventListener('change', function () {
                    if (allMovies.length > 0) loadMovies(view);
                });
            }
            if (libraryFilter) {
                libraryFilter.addEventListener('change', function () {
                    if (allMovies.length > 0) loadMovies(view);
                });
            }

            var btnRefresh = view.querySelector('#btnRefresh');
            if (btnRefresh) {
                btnRefresh.addEventListener('click', function () {
                    refreshMovieMetadata(view);
                });
            }

            if (btnMerge) {
                btnMerge.addEventListener('click', function () {
                    doMerge(view);
                });
            }

            if (modeLocal) {
                modeLocal.addEventListener('click', function () {
                    selectMode(view, 'local');
                });
            }
            if (modeCloud) {
                modeCloud.addEventListener('click', function () {
                    selectMode(view, 'cloud');
                });
            }

            log(view, 'Pret - Selectionnez les filtres puis cliquez sur Charger');
        });
    };
});
