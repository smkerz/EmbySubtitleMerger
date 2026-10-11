/**
 * SubtitleMerger - Emby UI Injector
 * Injecte une section "Fusionner les sous-titres" dans la page de sous-titres d'un film
 */
(function() {
    'use strict';

    console.log('[SubMerger] UI Injector loaded v9.15.0');

    var INJECTOR_ID = 'subtitleMergerSection';
    var currentItemId = null;
    var subtitles = [];
    var mergeJob = null;            // Id de la fusion en cours cote serveur
    var mergePollTimer = null;
    var mergePollFailures = 0;
    var mergeStarted = 0;

    // Observer pour detecter les changements de page
    function setupObserver() {
        var observer = new MutationObserver(function(mutations) {
            checkAndInject();
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });

        // Verifier aussi au chargement
        checkAndInject();
    }

    // Verifier si on est sur une page de sous-titres et injecter si necessaire
    function checkAndInject() {
        // Chercher le dialog/page de sous-titres
        var subtitleDialog = document.querySelector('.subtitleEditorDialog, .dialog[data-title*="Subtitle"], .dialogContainer');
        if (!subtitleDialog) return;

        // Chercher la section "Rechercher des sous-titres"
        var searchSection = subtitleDialog.querySelector('.subtitleSearchContainer, [class*="subtitleSearch"], .searchSubtitles');
        if (!searchSection) {
            // Essayer de trouver par le texte
            var allDivs = subtitleDialog.querySelectorAll('div, section');
            for (var i = 0; i < allDivs.length; i++) {
                var text = allDivs[i].textContent || '';
                if (text.indexOf('Rechercher des sous-titres') !== -1 || text.indexOf('Search for subtitles') !== -1) {
                    searchSection = allDivs[i];
                    break;
                }
            }
        }

        // Ne pas re-injecter si deja present
        if (document.getElementById(INJECTOR_ID)) return;

        // Extraire l'itemId de l'URL ou du contexte
        var itemId = extractItemId();
        if (!itemId) return;

        currentItemId = itemId;

        // Charger les sous-titres et injecter la section
        loadSubtitlesAndInject(subtitleDialog, searchSection, itemId);
    }

    // Extraire l'itemId depuis l'URL ou le DOM
    function extractItemId() {
        // Depuis l'URL
        var match = window.location.href.match(/[?&]id=([a-f0-9]+)/i);
        if (match) return match[1];

        // Depuis le hash
        match = window.location.hash.match(/id=([a-f0-9]+)/i);
        if (match) return match[1];

        // Depuis le pathname
        match = window.location.pathname.match(/\/item\/([a-f0-9]+)/i);
        if (match) return match[1];

        // Depuis un attribut data
        var itemElement = document.querySelector('[data-itemid], [data-id]');
        if (itemElement) {
            return itemElement.dataset.itemid || itemElement.dataset.id;
        }

        return null;
    }

    // ---------- Pistes de sous-titres ----------

    function escapeHtml(text) {
        return String(text == null ? '' : text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function readSubtitles(item) {
        var list = [];
        var streams = item.MediaStreams || [];
        for (var i = 0; i < streams.length; i++) {
            var s = streams[i];
            if (s.Type === 'Subtitle') {
                list.push({
                    index: s.Index,
                    language: s.Language || '',
                    codec: s.Codec || '?',
                    isExternal: !!s.IsExternal,
                    isText: !!s.IsTextSubtitleStream,
                    isForced: !!s.IsForced || /forced/i.test(s.Title || ''),
                    path: s.Path || '',
                    title: s.Title || ''
                });
            }
        }
        return list;
    }

    // Meme convention que la page du plugin : [OK] utilisable, [!] avec une reserve, [X] non utilisable
    function describe(sub) {
        if (!sub.isText) return { mark: '[X]', note: 'image, pas de texte à fusionner' };
        if (sub.isExternal && !sub.path) return { mark: '[X]', note: 'fichier externe introuvable' };
        if (sub.isForced) return { mark: '[!]', note: 'forced : quelques répliques seulement' };
        return { mark: '[OK]', note: '' };
    }

    function optionsHtml() {
        var html = '<option value="">-- Choisir --</option>';
        for (var i = 0; i < subtitles.length; i++) {
            var s = subtitles[i];
            var info = describe(s);
            var label = info.mark + ' #' + s.index + ' ' + (s.language || 'und').toUpperCase() + ' (' + s.codec + ')'
                + (s.isExternal ? ' [EXT]' : ' [INT]')
                + (s.title ? ' - ' + s.title : '')
                + (info.note ? ' : ' + info.note : '');
            html += '<option value="' + i + '"' + (info.mark === '[X]' ? ' disabled' : '') + '>' + escapeHtml(label) + '</option>';
        }
        return html;
    }

    // Charger les sous-titres depuis l'API Emby
    function loadSubtitlesAndInject(container, afterElement, itemId) {
        var apiClient = window.ApiClient;
        if (!apiClient) {
            console.log('[SubMerger] ApiClient not available');
            return;
        }

        var url = apiClient.getUrl('Items/' + itemId, {
            Fields: 'MediaStreams,Path'
        });

        apiClient.getJSON(url).then(function(item) {
            console.log('[SubMerger] Item loaded:', item.Name);
            subtitles = readSubtitles(item);
            injectMergeSection(container, afterElement, item);
        }).catch(function(err) {
            console.error('[SubMerger] Error loading item:', err);
        });
    }

    // ---------- Section de fusion ----------

    // Les couleurs de texte sont celles du theme d'Emby : la section reste lisible en clair comme en sombre
    var FIELD_STYLE = 'width:100%; padding:8px; background:rgba(128,128,128,0.15); border:1px solid rgba(128,128,128,0.5); border-radius:5px; color:inherit; box-sizing:border-box;';
    var LABEL_STYLE = 'display:block; margin-bottom:5px; opacity:0.75;';

    function injectMergeSection(container, afterElement, item) {
        var section = document.createElement('div');
        section.id = INJECTOR_ID;
        section.className = 'verticalSection';
        section.style.cssText = 'margin-top: 2em; padding: 1em; background: rgba(0,164,220,0.1); border-radius: 8px; border: 1px solid rgba(0,164,220,0.3);';

        var html = '<h2 class="sectionTitle" style="color: #00a4dc; margin: 0 0 1em 0;">Fusionner les sous-titres</h2>';

        if (subtitles.length < 2) {
            html += '<p style="color: #ff9800;">Il faut au moins 2 sous-titres pour pouvoir les fusionner.</p>';
        } else {
            html += '<div style="display: grid; grid-template-columns: 1fr 1fr; gap: 15px; margin-bottom: 15px;">';
            html += '<div><label style="' + LABEL_STYLE + '">Sous-titre 1 (langue 1, en haut)</label>';
            html += '<select id="mergeSub1" style="' + FIELD_STYLE + '">' + optionsHtml() + '</select></div>';
            html += '<div><label style="' + LABEL_STYLE + '">Sous-titre 2 (langue 2, en bas)</label>';
            html += '<select id="mergeSub2" style="' + FIELD_STYLE + '">' + optionsHtml() + '</select></div>';
            html += '</div>';

            html += '<details style="margin-bottom: 15px;">';
            html += '<summary style="cursor: pointer; opacity:0.75;">Options avancées</summary>';
            html += '<div style="display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 10px; margin-top: 10px;">';
            html += '<div><label style="' + LABEL_STYLE + '">Disposition</label>';
            html += '<select id="mergeLayout" style="' + FIELD_STYLE + '">';
            html += '<option value="topbottom">Langue 1 en haut, langue 2 en bas</option><option value="stacked">Les deux en bas</option></select></div>';
            html += '<div><label style="' + LABEL_STYLE + '">Mode</label>';
            html += '<select id="mergeMode" style="' + FIELD_STYLE + '">';
            html += '<option value="all">Tous</option><option value="overlapping">Chevauchement</option><option value="primary">Priorité langue 1</option></select></div>';
            html += '<div><label style="' + LABEL_STYLE + '">Tolérance (ms)</label>';
            html += '<input type="number" id="mergeTolerance" value="700" style="' + FIELD_STYLE + '"></div>';
            html += '</div></details>';

            html += '<button id="btnMergeInject" type="button" style="background: linear-gradient(135deg, #00a4dc, #6366f1); color: white; border: none; padding: 12px 24px; border-radius: 8px; font-weight: 600; cursor: pointer; width: 100%;">';
            html += 'Fusionner les sous-titres</button>';

            html += '<div id="mergeResultInject" style="margin-top: 10px;"></div>';
        }

        section.innerHTML = html;

        // Inserer apres la section de recherche ou a la fin
        if (afterElement && afterElement.parentNode) {
            afterElement.parentNode.insertBefore(section, afterElement.nextSibling);
        } else {
            container.appendChild(section);
        }

        var btn = document.getElementById('btnMergeInject');
        if (btn) {
            btn.addEventListener('click', function() {
                doMerge(item);
            });
        }

        console.log('[SubMerger] Merge section injected');
    }

    // ---------- Fusion (tache de fond sur le serveur, suivie par interrogation) ----------

    function pluginUrl(name, query) {
        var apiClient = window.ApiClient;
        var url = apiClient.serverAddress() + '/EmbySubtitleMerger/' + name + '?api_key=' + apiClient.accessToken();
        return query ? url + '&' + query : url;
    }

    // Un proxy ou Emby peut repondre par une page d'erreur HTML : on le dit clairement
    function readJson(response) {
        return response.text().then(function(text) {
            try {
                return JSON.parse(text);
            } catch (e) {
                throw new Error('réponse inattendue du serveur (HTTP ' + response.status + ')');
            }
        });
    }

    function setMerging(running) {
        var btn = document.getElementById('btnMergeInject');
        if (!btn) return;
        btn.disabled = running;
        btn.style.opacity = running ? '0.6' : '1';
        btn.textContent = running ? 'Fusion en cours...' : 'Fusionner les sous-titres';
    }

    function finishMerge(item, data) {
        var result = document.getElementById('mergeResultInject');

        clearTimeout(mergePollTimer);
        mergeJob = null;
        setMerging(false);

        if (data.Success) {
            showResult(result, 'success', 'Fusion réussie ! ' + data.CueCount + ' sous-titres créés. Actualisation...');
            refreshAndReload(item, result, data);
        } else {
            showResult(result, 'error', 'Erreur : ' + data.Error);
        }
    }

    // L'extraction des pistes d'un gros film dure plusieurs minutes : le serveur travaille en tache de fond
    function pollMerge(item) {
        clearTimeout(mergePollTimer);
        if (!mergeJob) return;

        // La fenetre des sous-titres a ete fermee : la fusion continue sur le serveur, le suivi s'arrete
        var result = document.getElementById('mergeResultInject');
        if (!result) {
            mergeJob = null;
            return;
        }

        fetch(pluginUrl('MergeStatus', 'JobId=' + encodeURIComponent(mergeJob)))
            .then(readJson)
            .then(function(data) {
                mergePollFailures = 0;
                if (!mergeJob) return;

                if (!data.Running) {
                    finishMerge(item, data);
                    return;
                }

                var seconds = Math.round((Date.now() - mergeStarted) / 1000);
                showResult(result, 'info', (data.Step || 'Fusion en cours') + '... ' + seconds + ' s'
                    + (seconds >= 30 ? ' (plusieurs minutes pour un gros film)' : ''));
                mergePollTimer = setTimeout(function() { pollMerge(item); }, 2000);
            })
            .catch(function(err) {
                console.error('[SubMerger] Merge status error:', err);
                if (++mergePollFailures >= 10) {
                    finishMerge(item, { Success: false, Error: 'suivi de la fusion impossible (' + err.message
                        + '). Elle continue peut-être sur le serveur.' });
                } else {
                    mergePollTimer = setTimeout(function() { pollMerge(item); }, 3000);
                }
            });
    }

    function doMerge(item) {
        var sub1 = document.getElementById('mergeSub1');
        var sub2 = document.getElementById('mergeSub2');
        var result = document.getElementById('mergeResultInject');

        if (!sub1 || !sub2 || mergeJob) return;

        if (sub1.value === '' || sub2.value === '') {
            showResult(result, 'error', 'Sélectionnez 2 sous-titres');
            return;
        }
        if (sub1.value === sub2.value) {
            showResult(result, 'error', 'Choisissez 2 sous-titres différents');
            return;
        }

        var first = subtitles[parseInt(sub1.value)];
        var second = subtitles[parseInt(sub2.value)];
        var tolerance = parseInt(document.getElementById('mergeTolerance').value);

        setMerging(true);
        mergeStarted = Date.now();
        mergePollFailures = 0;
        showResult(result, 'info', 'Démarrage...');

        fetch(pluginUrl('Merge'), {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                VideoPath: item.Path,
                PrimaryIndex: first.index,
                SecondaryIndex: second.index,
                Primary1IsExternal: first.isExternal,
                Primary1Path: first.path,
                Primary2IsExternal: second.isExternal,
                Primary2Path: second.path,
                Language1: first.language,
                Language2: second.language,
                Layout: document.getElementById('mergeLayout').value,
                Mode: document.getElementById('mergeMode').value,
                ToleranceMs: isNaN(tolerance) ? 700 : tolerance,
                UseCloudApi: false,
                Background: true
            })
        })
        .then(readJson)
        .then(function(data) {
            if (data.Running && data.JobId) {
                mergeJob = data.JobId;
                pollMerge(item);
            } else {
                finishMerge(item, data);
            }
        })
        .catch(function(err) {
            finishMerge(item, { Success: false, Error: err.message });
        });
    }

    // Rafraichir les metadonnees Emby et recharger les sous-titres
    function refreshAndReload(item, resultEl, mergeData) {
        var apiClient = window.ApiClient;
        if (!apiClient) return;

        var itemId = currentItemId;
        var done = 'Fusion réussie ! ' + mergeData.CueCount + ' sous-titres créés. ';

        // FullRefresh force Emby a detecter le nouveau fichier
        var refreshUrl = apiClient.serverAddress() + '/emby/Items/' + itemId
            + '/Refresh?Recursive=false&MetadataRefreshMode=FullRefresh&ImageRefreshMode=Default&ReplaceAllMetadata=false&ReplaceAllImages=false&api_key='
            + apiClient.accessToken();

        fetch(refreshUrl, { method: 'POST' })
            .then(function() {
                // Laisser a Emby le temps de traiter l'actualisation
                return new Promise(function(resolve) {
                    setTimeout(resolve, 4000);
                });
            })
            .then(function() {
                return apiClient.getJSON(apiClient.getUrl('Items/' + itemId, { Fields: 'MediaStreams,Path' }));
            })
            .then(function(updatedItem) {
                subtitles = readSubtitles(updatedItem);
                updateSubtitleSelects();
                showResult(resultEl, 'success', done + 'Liste actualisée.');
            })
            .catch(function(err) {
                console.error('[SubMerger] Refresh/reload error:', err);
                showResult(resultEl, 'success', done + 'Rechargez la page pour voir le nouveau sous-titre.');
            });
    }

    // La liste a change (un sous-titre en plus) : les choix precedents ne designent plus forcement la meme piste
    function updateSubtitleSelects() {
        var sub1 = document.getElementById('mergeSub1');
        var sub2 = document.getElementById('mergeSub2');
        if (!sub1 || !sub2) return;

        sub1.innerHTML = optionsHtml();
        sub2.innerHTML = optionsHtml();
    }

    // Afficher un resultat
    function showResult(el, type, msg) {
        if (!el) return;
        var colors = {
            success: { bg: 'rgba(76,175,80,0.2)', border: '#4CAF50' },
            error: { bg: 'rgba(244,67,54,0.2)', border: '#f44336' },
            info: { bg: 'rgba(255,152,0,0.2)', border: '#ff9800' }
        };
        var c = colors[type] || colors.info;
        el.innerHTML = '<div style="padding: 10px; background: ' + c.bg + '; border-left: 3px solid ' + c.border + '; border-radius: 4px;">' + escapeHtml(msg) + '</div>';
    }

    // Demarrer l'observer quand le DOM est pret
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', setupObserver);
    } else {
        setupObserver();
    }

})();
