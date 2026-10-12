# Emby Subtitle Merger

Emby plugin that builds bilingual subtitles: it merges two subtitle tracks into a single SRT file, and can
also translate a track, convert ASS/SSA subtitles to SRT, and tidy up the files it created.

<img width="213" height="513" alt="image" src="https://github.com/user-attachments/assets/04ef6a7e-f7f6-4295-b583-137868c4ef5f" /><img width="1605" height="718" alt="image" src="https://github.com/user-attachments/assets/cc635607-261d-494e-8a1c-6210e0de1e08" />

> The screenshots above show an older version. The page is now organised in four tabs and its texts are in French.

## Features

### Merge (tab "Fusionner")
- Merge any two text subtitles of a movie or episode: tracks embedded in the video (SRT, ASS/SSA, MOV_TEXT, WebVTT)
  or external `.srt` / `.ass` / `.ssa` files. Styled tracks are converted and cleaned on the fly.
- **Layout**: language 1 at the top and language 2 at the bottom, or both stacked at the bottom
  (use the latter on players that ignore the top position tag).
- **Merge modes**: all cues, only cues that have a counterpart in the other language, or language 1 complete
  with language 2 alongside it.
- The file is named after both languages, e.g. `Movie.Français + English.fr.srt`, so the player shows what it is.
  It can optionally be marked `default` so Emby selects it automatically.
- Runs as a background job with progress and a cancel button: extracting tracks from a large file takes minutes.
- Also available from the subtitle editor dialog of a movie.

### Translate (tab "Traduire")
- Translate a whole subtitle track and optionally build the bilingual file in one go.
- Two engines: **Google Translate** (free, no key, unofficial web endpoint, sentence by sentence) or
  **Claude** (paid Anthropic API key, context-aware).
- An interrupted translation resumes where it stopped. An existing subtitle is never overwritten without consent.

### Normalise (tab "Normaliser")
- Converts ASS/SSA tracks, which most TVs and mobile apps cannot display, into `.srt` files next to the videos,
  for one item or for all libraries. A track is skipped when the video already has a text subtitle in that language.
- Can run automatically as an Emby scheduled task ("Normaliser les sous-titres"), which has no trigger by default.
- **Clean-up**: removes generated copies, converted files, bilingual files and the "Normalisé pour Emby" note,
  always with a preview first. Original subtitles are never deleted.

### Maintenance (tab "Maintenance")
- **Batch merge**: create the bilingual subtitle of every video of a folder (a season) or of all libraries
  that has both languages.
- Clean-up of all libraries.


## Installation

### Download
1. Download `EmbySubtitleMerger.dll` from [Releases](../../releases)
2. Copy the file to Emby's plugin folder:
   - **Windows**: `%ProgramData%\Emby-Server\plugins\`
   - **Linux**: `/var/lib/emby/plugins/`
   - **Docker**: `/config/plugins/`
   - **Synology package**: `/volume1/@appdata/EmbyServer/plugins/` (owner `emby:emby`)
3. Restart Emby Server

### Build from Source
```bash
cd ClassLibrary1
dotnet build --configuration Release
```
The `EmbySubtitleMerger.dll` file is generated in `ClassLibrary1/bin/Release/net6.0/`.
The project references the Emby assemblies of a local Emby Server installation (see the `HintPath` entries in the `.csproj`).

## Usage

1. Open **Subtitle Merger** in the Emby server menu
2. Pick a library, click **Charger la liste**, then select a movie or episode
3. In the **Fusionner** tab, choose the two subtitles and click **Fusionner les sous-titres**

The new subtitle is written next to the video file and Emby is asked to refresh the item.

## Requirements

- Emby Server 4.7+ (.NET 6.0 compatible). The plugin's endpoints require an administrator session.
- **ffmpeg / ffprobe**: the ones shipped with Emby are used automatically.
- **MKVToolNix** (`mkvpropedit`), optional: only needed to remove the "Normalisé pour Emby" note from the track titles of `.mkv` files.

## Project Structure

```
EmbySubtitleMerger/
├── ClassLibrary1/
│   ├── Plugin.cs                      # Plugin, pages and configuration
│   ├── SubtitleMergerEntryPoint.cs    # Start-up: locates Emby's ffmpeg
│   ├── Configuration/
│   │   ├── configPage.html / .js      # Plugin page (four tabs)
│   │   └── subtitlePageInjector.js    # Merge section in the subtitle editor dialog
│   └── Subtitles/
│       ├── MergeService.cs            # Merge endpoints (background job)
│       ├── SubtitleMerger.cs          # Merge logic, layouts and modes
│       ├── SubtitleSource.cs          # Gets any track as a clean SRT
│       ├── SubtitleNaming.cs          # File naming, language catalogue and detection
│       ├── TranslateService.cs        # Translation endpoints
│       ├── TranslationService.cs      # Batching, alignment checks, resume
│       ├── ClaudeTranslationClient.cs # Claude engine
│       ├── GoogleTranslateClient.cs   # Free Google Translate engine
│       ├── NormalizeService.cs        # Normalise / clean-up / batch merge endpoints
│       ├── SubtitleNormalizer.cs      # Library walk, conversions, clean-up
│       ├── NormalizeNaming.cs         # Naming rules of generated files
│       ├── BatchMerger.cs             # Track selection for batch merge
│       ├── NormalizeScheduledTask.cs  # Emby scheduled task

│       ├── SettingsService.cs         # Translation settings stored on the server
│       ├── Jobs.cs                    # Background jobs
│       ├── ExternalTools.cs           # ffmpeg, ffprobe, mkvpropedit
│       └── SrtParser.cs               # SRT reader / writer
├── TODO.md
└── README.md
```

## Changelog

### v9.18
- Clean-up only recognises known bilingual name forms; conversions go through temporary files, so a cancelled run leaves no truncated subtitle
- Batch merge: automatic translations are stored as `Movie.Traduction-auto(engine).xx.srt` and never used as a translation source when an original exists; the preview announces the translations and the engine
- Google engine sends about three times fewer requests

### v9.13 – v9.17
- Batch merge can translate the missing language(s); scope "selected library"
- Removed tools that had become useless: legacy rename, note tagging, text track extraction, DoubleSub.io cloud mode
- French accents in the page

### v9.12
- Batch merge for a folder or all libraries
- Normalisation reads each video once, whatever the number of tracks
- Translation settings stored on the server

### v9.11
- Endpoints require an administrator session; removed the pages that embedded an Emby API key
- Translation resumes after an interruption; "default" option for bilingual files

### v9.10
- Free translation engine (Google Translate), Emby scheduled task for normalisation, cancel button for merges

### v9.1 – v9.9
- ASS/SSA normalisation and clean-up, merge as a background job, tabbed page, light theme,
  per-track warnings, merge section in the subtitle editor dialog

### v9.0
- AI translation, bilingual file naming, top/bottom layout

### v8.6
- TV series support (episodes), filters by library and media type, text search, auto-refresh after merge

## License

MIT License

## Author

Created with Claude Code.
