# Script simple pour generer la page HTML (ASCII-safe, no emojis)
$ErrorActionPreference = 'Stop'
Write-Host "[Info] Recuperation des films depuis l'API..." -ForegroundColor Green

# Ensure HtmlEncode is available for safe ASCII output
[void][System.Reflection.Assembly]::LoadWithPartialName('System.Web')
function Encode-Html([string]$text) {
    if ($null -eq $text) { return '' }
    return [System.Web.HttpUtility]::HtmlEncode($text)
}

$apiUrl = "http://localhost:8096/emby/Users/e69b7a5d73a943b4918712599f79905e/Items?IncludeItemTypes=Movie&Recursive=true&SortOrder=Ascending&SortBy=SortName&Fields=Overview,ProductionYear,Path&api_key=a482e26698584ef8a83d6ff49c0d8676"

$response = Invoke-RestMethod -Uri $apiUrl -Method Get
$movies = $response.Items
$totalCount = $response.TotalRecordCount

Write-Host "[OK] $totalCount films recuperes !" -ForegroundColor Green

# Afficher les films trouvés
Write-Host "Films trouves:" -ForegroundColor Cyan
foreach ($movie in $movies) {
    Write-Host "  - $($movie.Name) ($($movie.ProductionYear))" -ForegroundColor White
}

# Generate HTML (ASCII only; dynamic text HTML-encoded)
$htmlContent = @"
<div data-role="page" class="page type-interior pluginConfigurationPage" data-require="emby-input,emby-button" style="background:transparent; position:relative; color:#e6eaf0;">
    <div aria-hidden="true" style="position:absolute; inset:0; background:transparent; z-index:0;"></div>
    <div data-role="content">
        <div class="content-primary" style="background:rgba(23,28,34,0.76); padding:20px; border-radius:10px; position:relative; z-index:1; box-shadow:0 8px 20px rgba(0,0,0,0.4); backdrop-filter: blur(2px);">
            <h1>Plugin Films Emby - Version 23.2.0 - LISTE GENEREE</h1>
            <div class="verticalSection">
                <h2 class="sectionTitle">$totalCount films recuperes depuis votre API</h2>
                <div class="fieldDescription">
                    <div style="background:rgba(30,122,70,0.35); color:#eafff2; padding: 1.25em 1.5em; border-radius: 10px; margin: 1em 0; border:1px solid rgba(45,167,99,0.6);">
                        <h3 style="margin: 0 0 1em 0;">Liste generee automatiquement</h3>
                        <p style="margin: 0;">Vos <strong>$totalCount films</strong> ont ete recuperes depuis votre API JSON.</p>
                    </div>
                </div>
            </div>
            <div class="verticalSection">
                <h2 class="sectionTitle">Vos films</h2>
                <div class="fieldDescription">
                    <div style="background:rgba(15,20,25,0.5); padding: 1.25em; border-radius: 10px; margin: 1em 0; border:1px solid rgba(31,42,52,0.7);">
"@

# Ajouter chaque film
$index = 0
foreach ($movie in $movies) {
    $nameRaw = if ($movie.Name) { [string]$movie.Name } else { 'Titre inconnu' }
    $year = if ($movie.ProductionYear) { " (" + [string]$movie.ProductionYear + ")" } else { "" }
    $overviewRaw = if ($movie.Overview) { [string]$movie.Overview } else { 'Pas de description disponible' }
    if ($overviewRaw.Length -gt 100) { $overviewRaw = $overviewRaw.Substring(0, 100) + '...' }
    $fileNameRaw = Split-Path $movie.Path -Leaf

    $name = Encode-Html $nameRaw
    $overview = Encode-Html $overviewRaw
    $fileName = Encode-Html $fileNameRaw

    $bgColor = if ($index % 2 -eq 0) { "rgba(31,40,49,0.55)" } else { "rgba(26,34,43,0.55)" }

    if (-not $options) { $options = "" }
    $labelRaw = "$nameRaw$year"
    $label = Encode-Html $labelRaw
    $valueRaw = if ($movie.Id) { [string]$movie.Id } else { $fileNameRaw }
    $value = Encode-Html $valueRaw
    $options += @"
                        <option value="$value">$label</option>
"@

    $htmlContent += @"
                        <div style="margin: 1em 0; padding: 1em; background: $bgColor; border-radius: 10px; border-left: 4px solid rgba(45,167,99,0.85); box-shadow:0 4px 10px rgba(0,0,0,0.3);">
                            <div style="color:#f5f8fc; font-weight: 600; margin-bottom: 0.35em; font-size:1.05em;">$name$year</div>
                            <div style="color:#b9c3ce; font-size: 0.95em; margin-bottom: 0.5em; line-height:1.5;">$overview</div>
                            <div style="color:#8da0b3; font-size: 0.85em; font-family: monospace;">$fileName</div>
                        </div>
"@
    $index++
}

# Fermer le HTML (replace static select with dynamic iframe)
$htmlContent += @"
                        <p style="color: #4CAF50; margin: 1em 0 0 0; font-size: 0.9em;">
                            Total: $totalCount films | Derniere mise a jour: $(Get-Date -Format 'dd/MM/yyyy HH:mm')
                        </p>
                    </div>
                </div>
            </div>
            <div class="verticalSection">
                <h2 class="sectionTitle">Selectionner un film</h2>
                <div class="inputContainer">
                    <label class="inputLabel inputLabelUnfocused" for="moviesSelectStatic">Film (instantané):</label>
                    <select id="moviesSelectStatic" name="moviesSelectStatic" style="width:100%; max-width:600px; background:#0f1419; color:#e6eaf0; border:1px solid #2b3743; padding:8px; border-radius:8px;">
$options
                    </select>
                    <div class="fieldDescription" style="color:#9fb0c2;">Cette liste est generee a la construction.</div>
                </div>
            </div>
            <div class="verticalSection">
                <h2 class="sectionTitle">Selectionner un film (rafraichissable)</h2>
                <div class="inputContainer">
                    <div style="margin-top:8px; display:flex; align-items:center; gap:12px; flex-wrap:wrap;">
                        <a href="$([System.Web.HttpUtility]::HtmlAttributeEncode($apiUrl))" target="_blank" style="color:#7fc4ff; text-decoration:underline;">Voir le JSON</a>
                        <a class="emby-button raised button-submit" href="/EmbySubtitleMerger/MoviesDropdown.html?api_key=a482e26698584ef8a83d6ff49c0d8676&Mode=combo" target="moviesFrame" style="display:inline-block;">
                            <span>Rafraichir la combo</span>
                        </a>
                        <a class="emby-button raised" href="/EmbySubtitleMerger/MoviesDropdown.html?api_key=a482e26698584ef8a83d6ff49c0d8676&Mode=list&Size=12" target="moviesFrame" style="display:inline-block;">
                            <span>Basculer en liste</span>
                        </a>
                        <button class="emby-button raised" id="btnLoadSubtitles" type="button"><span>Charger sous-titres</span></button>
                    </div>
                    <iframe name="moviesFrame" id="moviesFrame" src="/EmbySubtitleMerger/MoviesDropdown.html?api_key=a482e26698584ef8a83d6ff49c0d8676&Mode=combo" style="width:100%; max-width:600px; height:240px; border:0; background:transparent; margin-top:8px;" title="Movies (live)"></iframe>
                    <div class="fieldDescription" style="color:#9fb0c2;">La combo ci-dessus est mise a jour en temps reel.</div>
                </div>
            </div>
            <div class="verticalSection">
                <h2 class="sectionTitle">Configuration</h2>
                <div class="inputContainer">
                    <label class="inputLabel inputLabelUnfocused" for="primarySubtitleSelect">Langue principale:</label>
                    <select id="primarySubtitleSelect" style="width:100%; max-width:600px; background:#0f1419; color:#e6eaf0; border:1px solid #2b3743; padding:8px; border-radius:8px;">
                        <option value="">— Sélectionner —</option>
                    </select>
                </div>
                <div class="inputContainer">
                    <label class="inputLabel inputLabelUnfocused" for="secondarySubtitleSelect">Langue secondaire:</label>
                    <select id="secondarySubtitleSelect" style="width:100%; max-width:600px; background:#0f1419; color:#e6eaf0; border:1px solid #2b3743; padding:8px; border-radius:8px;">
                        <option value="">— Sélectionner —</option>
                    </select>
                </div>
                <br/>
                <button is="emby-button" type="submit" class="raised button-submit emby-button">
                    <span>Sauvegarder</span>
                </button>
            </div>
        </div>
    </div>
</div>
"@

# Sauvegarder le fichier
$outHtml = Join-Path $PSScriptRoot "Configuration\configPage.html"
New-Item -ItemType Directory -Force -Path (Split-Path $outHtml -Parent) | Out-Null
$htmlContent | Set-Content $outHtml -Encoding UTF8
Write-Host "[OK] HTML sauvegarde !" -ForegroundColor Green

# Compiler et déployer
Write-Host "[Build] Compilation..." -ForegroundColor Yellow
dotnet build (Join-Path $PSScriptRoot "ClassLibrary1.csproj") -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "[Build] Echec de la compilation" -ForegroundColor Red
    exit 1
}

Write-Host "[Deploy] Deploiement..." -ForegroundColor Yellow
$dllPath = Join-Path $PSScriptRoot "bin\Release\net6.0\EmbySubtitleMerger.dll"
if (-not (Test-Path $dllPath)) {
    Write-Host "[Deploy] DLL introuvable: $dllPath" -ForegroundColor Red
    exit 1
}
Copy-Item $dllPath "C:\Users\smk20\AppData\Roaming\Emby-Server\programdata\plugins\EmbySubtitleMerger.dll" -Force

Write-Host "[Done] Plugin Version 23.2.0 deploye avec $totalCount films." -ForegroundColor Green

