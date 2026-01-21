/**
 * SubtitleMerger - Emby UI Injector
 * Injecte une section "Fusionner les sous-titres" dans la page de sous-titres d'un film
 */
(function() {
    'use strict';

    console.log('[SubMerger] UI Injector loaded v1.0');

    var INJECTOR_ID = 'subtitleMergerSection';
    var currentItemId = null;
    var subtitles = [];

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

            // Extraire les sous-titres
            subtitles = [];
            var streams = item.MediaStreams || [];
            for (var i = 0; i < streams.length; i++) {
                var s = streams[i];
                if (s.Type === 'Subtitle') {
                    subtitles.push({
                        index: s.Index,
                        language: s.Language || 'und',
                        codec: s.Codec || '?',
                        isExternal: s.IsExternal,
                        isText: s.IsTextSubtitleStream,
                        title: s.Title || ''
                    });
                }
            }

            // Injecter la section
            injectMergeSection(container, afterElement, item);
        }).catch(function(err) {
            console.error('[SubMerger] Error loading item:', err);
        });
    }

    // Injecter la section de fusion
    function injectMergeSection(container, afterElement, item) {
        var section = document.createElement('div');
        section.id = INJECTOR_ID;
        section.className = 'verticalSection';
        section.style.cssText = 'margin-top: 2em; padding: 1em; background: rgba(0,164,220,0.1); border-radius: 8px; border: 1px solid rgba(0,164,220,0.3);';

        var html = '<h2 class="sectionTitle" style="color: #00a4dc; margin: 0 0 1em 0;">';
        html += '<span style="margin-right: 8px;">&#x1F500;</span>Fusionner les sous-titres</h2>';

        if (subtitles.length < 2) {
            html += '<p style="color: #ff9800;">Il faut au moins 2 sous-titres pour pouvoir les fusionner.</p>';
        } else {
            html += '<div style="display: grid; grid-template-columns: 1fr 1fr; gap: 15px; margin-bottom: 15px;">';

            // Select 1
            html += '<div>';
            html += '<label style="color: #9fb0c2; display: block; margin-bottom: 5px;">Sous-titre 1 (HAUT)</label>';
            html += '<select id="mergeSub1" style="width: 100%; padding: 8px; background: rgba(0,0,0,0.3); border: 1px solid #444; border-radius: 5px; color: #e6eaf0;">';
            html += '<option value="">-- Choisir --</option>';
            for (var i = 0; i < subtitles.length; i++) {
                var s = subtitles[i];
                var label = '#' + s.index + ' ' + s.language.toUpperCase() + ' (' + s.codec + ')';
                label += s.isExternal ? ' [EXT]' : ' [INT]';
                if (!s.isText) label += ' [IMAGE]';
                html += '<option value="' + s.index + '">' + label + '</option>';
            }
            html += '</select></div>';

            // Select 2
            html += '<div>';
            html += '<label style="color: #9fb0c2; display: block; margin-bottom: 5px;">Sous-titre 2 (BAS)</label>';
            html += '<select id="mergeSub2" style="width: 100%; padding: 8px; background: rgba(0,0,0,0.3); border: 1px solid #444; border-radius: 5px; color: #e6eaf0;">';
            html += '<option value="">-- Choisir --</option>';
            for (var j = 0; j < subtitles.length; j++) {
                var s2 = subtitles[j];
                var label2 = '#' + s2.index + ' ' + s2.language.toUpperCase() + ' (' + s2.codec + ')';
                label2 += s2.isExternal ? ' [EXT]' : ' [INT]';
                if (!s2.isText) label2 += ' [IMAGE]';
                html += '<option value="' + s2.index + '">' + label2 + '</option>';
            }
            html += '</select></div>';

            html += '</div>';

            // Options avancees (collapsed)
            html += '<details style="margin-bottom: 15px;">';
            html += '<summary style="color: #9fb0c2; cursor: pointer;">Options avancees</summary>';
            html += '<div style="display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-top: 10px; padding: 10px; background: rgba(0,0,0,0.2); border-radius: 5px;">';
            html += '<div><label style="color: #888; font-size: 0.85em;">Mode</label>';
            html += '<select id="mergeMode" style="width:100%; padding:5px; background:rgba(0,0,0,0.3); border:1px solid #333; border-radius:4px; color:#e6eaf0;">';
            html += '<option value="all">Tous</option><option value="overlapping">Chevauchement</option><option value="primary">Priorite langue 1</option></select></div>';
            html += '<div><label style="color: #888; font-size: 0.85em;">Tolerance (ms)</label>';
            html += '<input type="number" id="mergeTolerance" value="700" style="width:100%; padding:5px; background:rgba(0,0,0,0.3); border:1px solid #333; border-radius:4px; color:#e6eaf0;"></div>';
            html += '</div></details>';

            // Bouton fusionner
            html += '<button id="btnMergeInject" type="button" style="background: linear-gradient(135deg, #00a4dc, #6366f1); color: white; border: none; padding: 12px 24px; border-radius: 8px; font-weight: 600; cursor: pointer; width: 100%;">';
            html += 'Fusionner les sous-titres</button>';

            // Zone resultat
            html += '<div id="mergeResultInject" style="margin-top: 10px;"></div>';
        }

        section.innerHTML = html;

        // Inserer apres la section de recherche ou a la fin
        if (afterElement && afterElement.parentNode) {
            afterElement.parentNode.insertBefore(section, afterElement.nextSibling);
        } else {
            container.appendChild(section);
        }

        // Attacher l'event listener au bouton
        var btn = document.getElementById('btnMergeInject');
        if (btn) {
            btn.addEventListener('click', function() {
                doMerge(item);
            });
        }

        console.log('[SubMerger] Merge section injected');
    }

    // Executer la fusion
    function doMerge(item) {
        var sub1 = document.getElementById('mergeSub1');
        var sub2 = document.getElementById('mergeSub2');
        var result = document.getElementById('mergeResultInject');
        var btn = document.getElementById('btnMergeInject');

        if (!sub1 || !sub2) return;

        var v1 = sub1.value;
        var v2 = sub2.value;

        if (!v1 || !v2) {
            showResult(result, 'error', 'Selectionnez 2 sous-titres');
            return;
        }
        if (v1 === v2) {
            showResult(result, 'error', 'Choisissez 2 sous-titres differents');
            return;
        }

        var mode = document.getElementById('mergeMode');
        var tolerance = document.getElementById('mergeTolerance');

        btn.disabled = true;
        btn.textContent = 'Fusion en cours...';
        showResult(result, 'info', 'Traitement en cours...');

        var apiClient = window.ApiClient;
        var apiKey = apiClient.accessToken();
        var baseUrl = apiClient.serverAddress();
        var url = baseUrl + '/EmbySubtitleMerger/Merge?api_key=' + apiKey;

        fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                VideoPath: item.Path,
                PrimaryIndex: parseInt(v1),
                SecondaryIndex: parseInt(v2),
                Mode: mode ? mode.value : 'all',
                ToleranceMs: tolerance ? parseInt(tolerance.value) : 700,
                UseCloudApi: false
            })
        })
        .then(function(r) { return r.json(); })
        .then(function(data) {
            btn.disabled = false;
            btn.textContent = 'Fusionner les sous-titres';

            if (data.Success) {
                showResult(result, 'success', 'Fusion reussie! ' + data.CueCount + ' sous-titres crees');
            } else {
                showResult(result, 'error', 'Erreur: ' + data.Error);
            }
        })
        .catch(function(err) {
            btn.disabled = false;
            btn.textContent = 'Fusionner les sous-titres';
            showResult(result, 'error', 'Erreur: ' + err.message);
        });
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
        el.innerHTML = '<div style="padding: 10px; background: ' + c.bg + '; border-left: 3px solid ' + c.border + '; border-radius: 4px;">' + msg + '</div>';
    }

    // Demarrer l'observer quand le DOM est pret
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', setupObserver);
    } else {
        setupObserver();
    }

})();
