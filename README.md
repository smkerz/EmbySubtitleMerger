# Emby Subtitle Merger

Emby plugin to merge two subtitles into a single bilingual SRT file (dual subtitles).

## Features

- **Subtitle Merging**: Combine two subtitles (e.g., French + English) into a single file
- **Movies & TV Shows Support**: Works with both movies and TV series episodes
- **Advanced Filters**: Filter by library, media type (movie/episode), and text search
- **Merge Modes**:
  - **All subtitles**: Merges both files completely
  - **Overlapping only**: Keeps only subtitles that overlap in time
  - **Primary language priority**: Uses primary language, fills gaps with secondary
- **Auto-extraction**: Automatically extracts embedded subtitles from video files (via ffmpeg)
- **Cloud Support**: Optional integration with DoubleSub.io API
- **Modern UI**: Built-in user interface integrated into Emby

## Installation

### Download
1. Download `EmbySubtitleMerger.dll` from [Releases](../../releases)
2. Copy the file to Emby's plugin folder:
   - **Windows**: `%ProgramData%\Emby-Server\plugins\`
   - **Linux**: `/var/lib/emby/plugins/`
   - **Docker**: `/config/plugins/`
3. Restart Emby Server

### Build from Source
```bash
cd ClassLibrary1
dotnet build --configuration Release
```
The `EmbySubtitleMerger.dll` file will be generated in `ClassLibrary1/bin/Release/net6.0/`

## Usage

1. Go to **Settings** > **Plugins** > **Subtitle Merger**
2. Click **Load** to list your media
3. Select a movie or episode
4. Choose the two subtitles to merge
5. Configure options (merge mode, tolerance, offsets)
6. Click **Merge**

The new subtitle will be created in the same folder as the video file.

## Merge Options

| Option | Description |
|--------|-------------|
| **Merge mode** | How to combine subtitles |
| **Tolerance (ms)** | Margin to consider two subtitles as simultaneous |
| **Subtitle 1 offset** | Adjust timing of first subtitle (in ms) |
| **Subtitle 2 offset** | Adjust timing of second subtitle (in ms) |

## Requirements

- Emby Server 4.7+ (.NET 6.0 compatible)
- **ffmpeg** installed and available in PATH (for embedded subtitle extraction)

## Project Structure

```
EmbySubtitleMerger/
├── ClassLibrary1/
│   ├── Class1.cs                 # Main plugin class
│   ├── SubtitleMergerPage.cs     # Merge API endpoint
│   ├── Configuration/
│   │   ├── configPage.html       # User interface
│   │   └── configPage.js         # JavaScript logic
│   └── Subtitles/
│       ├── MergeService.cs       # Merge service
│       ├── SrtParser.cs          # SRT file parser
│       ├── SubtitleMerger.cs     # Merge logic
│       ├── FfmpegHelper.cs       # ffmpeg extraction
│       └── DoubleSubApiClient.cs # DoubleSub.io API client
└── README.md
```

## Changelog

### v8.6
- TV series support (episodes)
- Filter by library and media type
- Text search
- Alphabetical sorting
- Auto-refresh after merge

## License

MIT License

## Author

Created with Claude Code.
