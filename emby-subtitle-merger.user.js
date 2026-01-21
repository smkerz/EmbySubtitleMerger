// ==UserScript==
// @name         Emby Subtitle Merger
// @namespace    https://doublesub.io
// @version      1.0
// @description  Ajoute un bouton "Fusionner les sous-titres" dans Emby
// @author       DoubleSub.io
// @match        http://localhost:8096/*
// @match        http://127.0.0.1:8096/*
// @match        https://*.emby.media/*
// @grant        none
// ==/UserScript==

(function() {
    'use strict';

    console.log('[SubMerger] Userscript loaded');

    const BUTTON_ID = 'subtitleMergerBtn';
    let lastUrl = '';

    // Observer pour détecter les changements de page
    function setupObserver() {
        const observer = new MutationObserver(function() {
            if (window.location.href !== lastUrl) {
                lastUrl = window.location.href;
                setTimeout(checkAndInject, 500);
            }
            checkAndInject();
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });

        // Vérifier au chargement initial
        setTimeout(checkAndInject, 1000);
    }

    // Vérifier si on est sur une page avec des sous-titres
    function checkAndInject() {
        // Ne pas re-injecter si déjà présent
        if (document.getElementById(BUTTON_ID)) return;

        // Chercher la section "Rechercher des sous-titres" ou les éléments de sous-titres
        const subtitleElements = document.querySelectorAll('.subtitleList, .listItem-body, [data-type="Subtitle"]');
        const searchButtons = document.querySelectorAll('button[is="paper-icon-button-light"]');

        // Chercher le conteneur de sous-titres dans un dialog ou une page
        let container = null;

        // Méthode 1: Dialog de sous-titres
        const dialog = document.querySelector('.dialog, .dialogContainer, .subtitleEditorDialog');
        if (dialog) {
            const subtitleSection = dialog.querySelector('.formDialogContent, .dialogContent');
            if (subtitleSection && subtitleSection.textContent.includes('Rechercher')) {
                container = subtitleSection;
            }
        }

        // Méthode 2: Chercher par texte
        if (!container) {
            const allDivs = document.querySelectorAll('div, section');
            for (let div of allDivs) {
                if (div.querySelector && div.textContent) {
                    const text = div.textContent;
                    if ((text.includes('Rechercher des sous-titres') || text.includes('Search for subtitles'))
                        && text.includes('Langue')) {
                        // Trouver le parent approprié
                        let parent = div;
                        for (let i = 0; i < 3; i++) {
                            if (parent.parentElement) parent = parent.parentElement;
                        }
                        container = parent;
                        break;
                    }
                }
            }
        }

        if (!container) return;

        // Extraire l'itemId
        const itemId = extractItemId();
        if (!itemId) return;

        console.log('[SubMerger] Found subtitle section, injecting button for item:', itemId);
        injectMergeButton(container, itemId);
    }

    // Extraire l'itemId depuis l'URL ou le contexte
    function extractItemId() {
        // Depuis l'URL - format: /item/xxx/subtitles ou ?id=xxx
        let match = window.location.href.match(/[?&]id=([a-f0-9]+)/i);
        if (match) return match[1];

        match = window.location.pathname.match(/\/item\/([a-f0-9]+)/i);
        if (match) return match[1];

        match = window.location.hash.match(/id=([a-f0-9]+)/i);
        if (match) return match[1];

        // Depuis l'URL du dialog - chercher dans les attributs data
        const itemElement = document.querySelector('[data-itemid], [data-id]');
        if (itemElement) {
            return itemElement.dataset.itemid || itemElement.dataset.id;
        }

        // Chercher dans l'URL du formulaire
        const form = document.querySelector('form[action*="Subtitles"]');
        if (form) {
            const actionMatch = form.action.match(/Items\/([a-f0-9]+)/i);
            if (actionMatch) return actionMatch[1];
        }

        return null;
    }

    // Injecter le bouton de fusion
    function injectMergeButton(container, itemId) {
        // Créer le conteneur de notre section
        const section = document.createElement('div');
        section.id = BUTTON_ID;
        section.style.cssText = `
            margin: 20px 0;
            padding: 15px;
            background: linear-gradient(135deg, rgba(0,164,220,0.1), rgba(99,102,241,0.1));
            border: 1px solid rgba(0,164,220,0.3);
            border-radius: 10px;
        `;

        section.innerHTML = `
            <div style="display: flex; align-items: center; gap: 10px; margin-bottom: 10px;">
                <span style="font-size: 1.3em;">🔀</span>
                <h3 style="margin: 0; color: #00a4dc; font-size: 1.1em;">Fusionner les sous-titres</h3>
            </div>
            <p style="color: #9fb0c2; margin: 0 0 15px 0; font-size: 0.9em;">
                Combinez deux sous-titres en un seul fichier bilingue avec DoubleSub.io
            </p>
            <button id="openMergerBtn" style="
                background: linear-gradient(135deg, #00a4dc, #6366f1);
                color: white;
                border: none;
                padding: 12px 24px;
                border-radius: 8px;
                font-weight: 600;
                cursor: pointer;
                font-size: 1em;
                transition: all 0.2s;
            ">
                Ouvrir l'outil de fusion
            </button>
        `;

        // Trouver où insérer (après "Rechercher des sous-titres" ou à la fin)
        const searchSection = container.querySelector('[class*="search"], button[type="submit"]');
        if (searchSection && searchSection.parentElement) {
            searchSection.parentElement.insertAdjacentElement('afterend', section);
        } else {
            container.appendChild(section);
        }

        // Event listener pour le bouton
        document.getElementById('openMergerBtn').addEventListener('click', function() {
            const mergeUrl = `/EmbySubtitleMerger/QuickMerge.html?ItemId=${itemId}`;

            // Ouvrir dans une popup
            const width = 650;
            const height = 700;
            const left = (screen.width - width) / 2;
            const top = (screen.height - height) / 2;

            window.open(
                mergeUrl,
                'SubtitleMerger',
                `width=${width},height=${height},left=${left},top=${top},resizable=yes,scrollbars=yes`
            );
        });

        // Effet hover
        const btn = document.getElementById('openMergerBtn');
        btn.addEventListener('mouseenter', () => {
            btn.style.transform = 'translateY(-2px)';
            btn.style.boxShadow = '0 5px 20px rgba(0,164,220,0.4)';
        });
        btn.addEventListener('mouseleave', () => {
            btn.style.transform = 'none';
            btn.style.boxShadow = 'none';
        });
    }

    // Démarrer
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', setupObserver);
    } else {
        setupObserver();
    }
})();
