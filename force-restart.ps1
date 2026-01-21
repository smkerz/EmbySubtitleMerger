#!/usr/bin/env pwsh
# Script de redémarrage forcé d'Emby Server pour charger la nouvelle version du plugin

Write-Host "🔥 REDÉMARRAGE FORCÉ D'EMBY SERVER" -ForegroundColor Red
Write-Host "=================================" -ForegroundColor Red

# 1. Arrêter Emby Server
Write-Host "1️⃣ Arrêt d'Emby Server..." -ForegroundColor Yellow
try {
    Get-Process "Emby.Server" -ErrorAction Stop | Stop-Process -Force
    Write-Host "✅ Emby Server arrêté avec succès" -ForegroundColor Green
    Start-Sleep -Seconds 3
} catch {
    Write-Host "⚠️ Emby Server n'était pas en cours d'exécution" -ForegroundColor Yellow
}

# 2. Vérifier que le processus est bien arrêté
Write-Host "2️⃣ Vérification de l'arrêt..." -ForegroundColor Yellow
$attempts = 0
while ((Get-Process "Emby.Server" -ErrorAction SilentlyContinue) -and ($attempts -lt 10)) {
    Write-Host "⏳ Attente de l'arrêt complet..." -ForegroundColor Yellow
    Start-Sleep -Seconds 2
    $attempts++
}

if (Get-Process "Emby.Server" -ErrorAction SilentlyContinue) {
    Write-Host "❌ ÉCHEC: Emby Server ne s'arrête pas. Tuez le processus manuellement." -ForegroundColor Red
    exit 1
}

Write-Host "✅ Emby Server complètement arrêté" -ForegroundColor Green

# 3. Recompiler et déployer le plugin
Write-Host "3️⃣ Recompilation du plugin..." -ForegroundColor Yellow
Set-Location "C:\Users\smk20\GitHub\EmbyTest\ClassLibrary1\ClassLibrary1"

dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ ÉCHEC de compilation" -ForegroundColor Red
    exit 1
}

Write-Host "4️⃣ Déploiement du plugin..." -ForegroundColor Yellow
Copy-Item "bin\Release\net6.0\EmbySubtitleMerger.dll" "C:\Users\smk20\AppData\Roaming\Emby-Server\programdata\plugins\EmbySubtitleMerger.dll" -Force

# Vérifier la taille et date du fichier déployé
$pluginFile = "C:\Users\smk20\AppData\Roaming\Emby-Server\programdata\plugins\EmbySubtitleMerger.dll"
if (Test-Path $pluginFile) {
    $fileInfo = Get-Item $pluginFile
    Write-Host "✅ Plugin déployé: $($fileInfo.Length) bytes, modifié le $($fileInfo.LastWriteTime)" -ForegroundColor Green
} else {
    Write-Host "❌ ÉCHEC: Plugin non déployé" -ForegroundColor Red
    exit 1
}

# 5. Redémarrer Emby Server
Write-Host "5️⃣ Redémarrage d'Emby Server..." -ForegroundColor Yellow
$embyPath = "C:\Users\smk20\AppData\Roaming\Emby-Server\system\EmbyServer.exe"

if (Test-Path $embyPath) {
    Start-Process $embyPath
    Write-Host "✅ Emby Server redémarré" -ForegroundColor Green
} else {
    Write-Host "❌ Impossible de trouver EmbyServer.exe à $embyPath" -ForegroundColor Red
    Write-Host "🔍 Démarrez Emby Server manuellement" -ForegroundColor Yellow
}

# 6. Attendre que le serveur soit prêt
Write-Host "6️⃣ Attente du démarrage complet..." -ForegroundColor Yellow
Start-Sleep -Seconds 15

# 7. Tester la connectivité
Write-Host "7️⃣ Test de connectivité..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:8096/emby/system/info/public" -TimeoutSec 10
    if ($response.StatusCode -eq 200) {
        Write-Host "✅ Emby Server opérationnel sur http://localhost:8096" -ForegroundColor Green
    }
} catch {
    Write-Host "⚠️ Serveur pas encore prêt, attendez quelques secondes de plus" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "🚀 REDÉMARRAGE TERMINÉ!" -ForegroundColor Green
Write-Host "📋 ÉTAPES SUIVANTES:" -ForegroundColor Cyan
Write-Host "   1. Ouvrez http://localhost:8096/web" -ForegroundColor White
Write-Host "   2. Allez dans Tableau de bord > Plugins" -ForegroundColor White
Write-Host "   3. Cliquez sur 'Subtitle Merger Plugin'" -ForegroundColor White
Write-Host "   4. Vérifiez que vous voyez 'Version 1.1.4'" -ForegroundColor White
Write-Host "   5. Ouvrez la console (F12) et cherchez nos messages" -ForegroundColor White
Write-Host ""
Write-Host "🔍 Messages attendus dans la console:" -ForegroundColor Cyan
Write-Host "   🎬 Plugin Films Emby v1.1.4 - Récupération directe des films" -ForegroundColor White
Write-Host "   🔍 Tentative de récupération des films via API..." -ForegroundColor White
Write-Host ""

