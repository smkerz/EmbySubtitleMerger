# Script d'installation du plugin Emby Hello World
# Exécutez ce script en tant qu'administrateur

param(
    [string]$EmbyPluginsPath = ""
)

Write-Host "=== Installation du Plugin Emby Hello World ===" -ForegroundColor Green

# Déterminer le chemin des plugins Emby
if ([string]::IsNullOrEmpty($EmbyPluginsPath)) {
    $possiblePaths = @(
        "$env:ProgramData\Emby-Server\plugins",
        "C:\ProgramData\Emby-Server\plugins",
        "$env:LOCALAPPDATA\Emby-Server\plugins"
    )
    
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            $EmbyPluginsPath = $path
            break
        }
    }
}

if ([string]::IsNullOrEmpty($EmbyPluginsPath)) {
    Write-Host "❌ Impossible de trouver le dossier des plugins Emby." -ForegroundColor Red
    Write-Host "Veuillez spécifier le chemin manuellement :" -ForegroundColor Yellow
    Write-Host ".\install-plugin.ps1 -EmbyPluginsPath 'C:\Chemin\Vers\Emby\plugins'" -ForegroundColor Cyan
    exit 1
}

Write-Host "📁 Dossier des plugins Emby trouvé : $EmbyPluginsPath" -ForegroundColor Green

# Chemin du fichier DLL à copier
$sourceDll = "ClassLibrary1\bin\Release\net8.0\EmbyHelloWorld.dll"
$targetDll = Join-Path $EmbyPluginsPath "EmbyHelloWorld.dll"

# Vérifier que le fichier source existe
if (-not (Test-Path $sourceDll)) {
    Write-Host "❌ Fichier source introuvable : $sourceDll" -ForegroundColor Red
    Write-Host "Veuillez d'abord compiler le projet : dotnet build --configuration Release" -ForegroundColor Yellow
    exit 1
}

# Sauvegarder l'ancien fichier s'il existe
if (Test-Path $targetDll) {
    $backupPath = $targetDll + ".backup." + (Get-Date -Format "yyyyMMdd-HHmmss")
    Write-Host "💾 Sauvegarde de l'ancien fichier : $backupPath" -ForegroundColor Yellow
    Copy-Item $targetDll $backupPath
}

# Copier le nouveau fichier
try {
    Write-Host "📋 Copie du plugin..." -ForegroundColor Yellow
    Copy-Item $sourceDll $targetDll -Force
    Write-Host "✅ Plugin copié avec succès !" -ForegroundColor Green
} catch {
    Write-Host "❌ Erreur lors de la copie : $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Vérifier que le service Emby est en cours d'exécution
$embyService = Get-Service -Name "EmbyServer" -ErrorAction SilentlyContinue
if ($embyService) {
    Write-Host "🔄 Redémarrage du service Emby..." -ForegroundColor Yellow
    try {
        Restart-Service -Name "EmbyServer" -Force
        Write-Host "✅ Service Emby redémarré avec succès !" -ForegroundColor Green
    } catch {
        Write-Host "⚠️  Impossible de redémarrer le service Emby automatiquement." -ForegroundColor Yellow
        Write-Host "Veuillez redémarrer manuellement le service Emby Server." -ForegroundColor Cyan
    }
} else {
    Write-Host "⚠️  Service Emby Server non trouvé." -ForegroundColor Yellow
    Write-Host "Veuillez redémarrer manuellement votre serveur Emby." -ForegroundColor Cyan
}

Write-Host ""
Write-Host "🎉 Installation terminée !" -ForegroundColor Green
Write-Host ""
Write-Host "📋 Prochaines étapes :" -ForegroundColor Cyan
Write-Host "1. Ouvrez l'interface web d'Emby" -ForegroundColor White
Write-Host "2. Allez dans Paramètres → Plugins" -ForegroundColor White
Write-Host "3. Trouvez 'Hello World Plugin' dans la liste" -ForegroundColor White
Write-Host "4. Cliquez sur Configuration pour voir la page Hello World" -ForegroundColor White
Write-Host ""
Write-Host "🔍 Si le plugin n'apparaît pas, vérifiez les logs Emby :" -ForegroundColor Yellow
Write-Host "   Paramètres → Logs" -ForegroundColor White
