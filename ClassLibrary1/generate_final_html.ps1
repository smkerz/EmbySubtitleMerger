# Script pour générer le HTML final avec les films depuis l'API
Write-Host "🎬 Génération du HTML final avec vos films..." -ForegroundColor Green

$apiKey = "a482e26698584ef8a83d6ff49c0d8676"
$userId = "e69b7a5d73a943b4918712599f79905e"
$apiUrl = "http://localhost:8096/emby/Users/$userId/Items?IncludeItemTypes=Movie&Recursive=true&SortOrder=Ascending&SortBy=SortName&Fields=Overview,ProductionYear,Path&api_key=$apiKey"

try {
    Write-Host "📡 Appel API Emby..." -ForegroundColor Yellow
    $response = Invoke-RestMethod -Uri $apiUrl -Method Get
    $movies = $response.Items
    $totalCount = $response.TotalRecordCount
    
    Write-Host "✅ $totalCount films récupérés !" -ForegroundColor Green
    
    # Générer le HTML des films
    $moviesHtml = ""
    $index = 0
    foreach ($movie in $movies) {
        $name = $movie.Name
        $year = if ($movie.ProductionYear) { " ($($movie.ProductionYear))" } else { "" }
        $overview = if ($movie.Overview) { 
            if ($movie.Overview.Length -gt 150) { 
                $movie.Overview.Substring(0, 150) + "..." 
            } else { 
                $movie.Overview 
            }
        } else { "Pas de description disponible" }
        $fileName = Split-Path $movie.Path -Leaf
        
        $bgColor = if ($index % 2 -eq 0) { "#333" } else { "#404040" }
        
        $moviesHtml += @"

                        <div style="margin: 1em 0; padding: 1em; background: $bgColor; border-radius: 6px; border-left: 4px solid #4CAF50;">
                            <div style="color: #fff; font-weight: bold; margin-bottom: 0.5em;">🎬 $name$year</div>
                            <div style="color: #ccc; font-size: 0.9em; margin-bottom: 0.5em;">
                                "$overview"
                            </div>
                            <div style="color: #888; font-size: 0.8em; font-family: monospace;">📁 $fileName</div>
                        </div>
"@
        $index++
    }
    
    # Générer le HTML complet
    $finalHtml = @"
<div data-role="page" class="page type-interior pluginConfigurationPage" data-require="emby-input,emby-button">
    <div data-role="content">
        <div class="content-primary">
            <h1>🎬 Plugin Films Emby - Version 10.0.0 - FINAL GÉNÉRÉ</h1>
            
            <div class="verticalSection">
                <h2 class="sectionTitle">✅ Plugin fonctionnel avec films dynamiques !</h2>
                <div class="fieldDescription">
                    <div style="background: #4CAF50; color: white; padding: 1.5em; border-radius: 8px; margin: 1em 0;">
                        <h3 style="margin: 0 0 1em 0;">🎉 Films générés depuis votre API JSON !</h3>
                        <p style="margin: 0;">Vos <strong>$totalCount films</strong> ont été récupérés automatiquement depuis votre API Emby.</p>
                    </div>
                </div>
            </div>
            
            <div class="verticalSection">
                <h2 class="sectionTitle">🎬 Vos $totalCount films Emby</h2>
                <div class="fieldDescription">
                    <div style="background: #2d5016; padding: 1em; border-radius: 4px; margin: 1em 0;">
                        <h5 style="color: #90EE90; margin: 0 0 0.5em 0;">✅ $totalCount films générés automatiquement :</h5>
                        $moviesHtml
                        
                        <p style="color: #4CAF50; margin: 1em 0 0 0; font-size: 0.9em;">
                            ✅ Total: $totalCount films | Générés automatiquement | $(Get-Date -Format 'dd/MM/yyyy HH:mm')
                        </p>
                        
                        <div style="margin: 1em 0; padding: 1em; background: #1a1a1a; border-radius: 4px; border-left: 4px solid #2196F3;">
                            <p style="color: #2196F3; margin: 0 0 0.5em 0;">🔗 <strong>API JSON directe :</strong></p>
                            <a href="$apiUrl" 
                               target="_blank" 
                               style="background: #2196F3; color: white; padding: 0.5em 1em; border: none; border-radius: 4px; cursor: pointer; text-decoration: none; display: inline-block;">
                                🌐 Voir le JSON complet de vos films
                            </a>
                        </div>
                    </div>
                </div>
            </div>
            
            <div class="verticalSection">
                <h2 class="sectionTitle">⚙️ Configuration du plugin</h2>
                <div class="inputContainer">
                    <label class="inputLabel inputLabelUnfocused" for="txtLanguage1">Langue principale:</label>
                    <input is="emby-input" type="text" id="txtLanguage1" value="en" />
                    <div class="fieldDescription">Langue principale pour les sous-titres</div>
                </div>
                <div class="inputContainer">
                    <label class="inputLabel inputLabelUnfocused" for="txtLanguage2">Langue secondaire:</label>
                    <input is="emby-input" type="text" id="txtLanguage2" value="fr" />
                    <div class="fieldDescription">Langue secondaire pour les sous-titres</div>
                </div>
                <br/>
                <button is="emby-button" type="submit" class="raised button-submit emby-button">
                    <span>💾 Sauvegarder la configuration</span>
                </button>
            </div>
        </div>
    </div>
</div>
"@

    # Sauvegarder le fichier HTML
    $finalHtml | Set-Content "Configuration\configPage.html" -Encoding UTF8
    
    Write-Host "✅ HTML généré avec $totalCount films !" -ForegroundColor Green
    
    # Recompiler et déployer
    Write-Host "🔨 Compilation..." -ForegroundColor Yellow
    dotnet build -c Release
    
    Write-Host "📦 Déploiement..." -ForegroundColor Yellow
    Copy-Item "bin\Release\net6.0\EmbySubtitleMerger.dll" "C:\Users\smk20\AppData\Roaming\Emby-Server\programdata\plugins\EmbySubtitleMerger.dll" -Force
    
    Write-Host "🎉 Plugin mis à jour avec vos $totalCount films !" -ForegroundColor Green
    Write-Host "Films trouvés:" -ForegroundColor Cyan
    foreach ($movie in $movies) {
        Write-Host "  - $($movie.Name) ($($movie.ProductionYear))" -ForegroundColor White
    }
    
    Write-Host ""
    Write-Host "🚀 Redémarrez Emby Server et rechargez votre plugin !" -ForegroundColor Magenta
    
} catch {
    Write-Host "❌ Erreur lors de l'appel API: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

