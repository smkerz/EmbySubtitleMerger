# Emby Subtitle Merger

Plugin Emby pour fusionner deux sous-titres en un seul fichier SRT bilingue (dual subtitles).

## Fonctionnalites

- **Fusion de sous-titres** : Combinez deux sous-titres (ex: francais + anglais) en un seul fichier
- **Support Films et Series** : Fonctionne avec les films et les episodes de series TV
- **Filtres avances** : Filtrez par mediatheque, type (film/serie) et recherche textuelle
- **Modes de fusion** :
  - **Tous les sous-titres** : Fusionne l'integralite des deux fichiers
  - **Chevauchement uniquement** : Ne garde que les sous-titres qui se chevauchent
  - **Priorite langue 1** : Utilise la langue principale, complete avec la seconde
- **Extraction automatique** : Extrait les sous-titres embarques dans les fichiers video (via ffmpeg)
- **Support cloud** : Integration optionnelle avec DoubleSub.io API
- **Interface moderne** : Interface utilisateur integree dans Emby

## Installation

### Telechargement
1. Telechargez `EmbySubtitleMerger.dll` depuis les [Releases](../../releases)
2. Copiez le fichier dans le dossier plugins d'Emby :
   - **Windows** : `%ProgramData%\Emby-Server\plugins\`
   - **Linux** : `/var/lib/emby/plugins/`
   - **Docker** : `/config/plugins/`
3. Redemarrez Emby Server

### Compilation depuis les sources
```bash
cd ClassLibrary1
dotnet build --configuration Release
```
Le fichier `EmbySubtitleMerger.dll` sera genere dans `ClassLibrary1/bin/Release/net6.0/`

## Utilisation

1. Allez dans **Parametres** > **Plugins** > **Subtitle Merger**
2. Cliquez sur **Charger** pour lister vos medias
3. Selectionnez un film ou episode
4. Choisissez les deux sous-titres a fusionner
5. Configurez les options (mode de fusion, tolerance, decalages)
6. Cliquez sur **Fusionner**

Le nouveau sous-titre sera cree dans le meme dossier que le fichier video.

## Options de fusion

| Option | Description |
|--------|-------------|
| **Mode de fusion** | Comment combiner les sous-titres |
| **Tolerance (ms)** | Marge pour considerer deux sous-titres comme simultanees |
| **Decalage sous-titre 1** | Ajuster le timing du premier sous-titre (en ms) |
| **Decalage sous-titre 2** | Ajuster le timing du second sous-titre (en ms) |

## Prerequis

- Emby Server 4.7+ (compatible .NET 6.0)
- **ffmpeg** installe et accessible dans le PATH (pour l'extraction des sous-titres embarques)

## Structure du projet

```
EmbySubtitleMerger/
├── ClassLibrary1/
│   ├── Class1.cs                 # Plugin principal
│   ├── SubtitleMergerPage.cs     # API endpoint de fusion
│   ├── Configuration/
│   │   ├── configPage.html       # Interface utilisateur
│   │   └── configPage.js         # Logique JavaScript
│   └── Subtitles/
│       ├── MergeService.cs       # Service de fusion
│       ├── SrtParser.cs          # Parser de fichiers SRT
│       ├── SubtitleMerger.cs     # Logique de fusion
│       ├── FfmpegHelper.cs       # Extraction via ffmpeg
│       └── DoubleSubApiClient.cs # Client API DoubleSub.io
└── README.md
```

## Changelog

### v8.6
- Support des series TV (episodes)
- Filtres par mediatheque et type de media
- Recherche textuelle
- Tri alphabetique des resultats
- Rafraichissement automatique apres fusion

## Licence

MIT License

## Auteur

Projet cree avec l'aide de Claude Code.
