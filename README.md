# Cartoon Cartoon Sundays

Cartoon Cartoon Fridays was an early 2000s Cartoon Network programming block with a variety of shows. This application emulates that by randomly populating shows into a VLC playlist with user constraints for time and show selectivity.

## VLC Folder Queue

Windows WPF app that builds a randomized, time-boxed watch queue from a
video library and hands it to VLC as an `.m3u` playlist.

- **Single media root**: point it at one folder (e.g. `E:\rec\TV`); it
  auto-discovers a show/movie per immediate subfolder, collapsing any depth
  of season subfolders into that show. Adding a different root replaces the
  old one (its discovered shows/files go with it; play history is kept,
  since it's keyed by file path).
- **Library tab**: exclude a show, mark it "Episodic," tag it, or manage
  per-file exclusions (specials/extras) via "Manage Files...". Launching the
  app (or clicking "Rescan Library") re-scans the root and highlights any
  newly discovered shows.
- **Episodic**: marking a show episodic doesn't queue the whole thing as one
  block — it makes that show contribute its *next unwatched episode* (in
  natural season/episode order) as a single candidate in the same random
  draw as everything else. Play it, and next time it offers the following
  episode.
- **History tab**: see what's already been played; mark items unplayed to
  put them back in rotation.
- **Queue Builder tab**: enter a target runtime in minutes, generate a
  randomized queue (±15% of the target) drawing from unplayed, non-excluded
  files, review/edit it, then send it to VLC. A candidate is only added if
  it fits within the tolerance, so one long file can't blow the budget.

Library data (folders, files, tags, play history) is stored as JSON at
`%AppData%\VlcFolderQueue\library.json`.

## Build & run

```bash
dotnet build
dotnet run --project VlcFolderQueue
```

```powershell
dotnet publish VlcFolderQueue -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Bundled ffprobe.exe

File durations (used for the time-window queue math) come from `ffprobe.exe`,
bundled in `VlcFolderQueue/tools/` (gitignored). Fetch it once after cloning:

```powershell
$rel = Invoke-RestMethod 'https://api.github.com/repos/GyanD/codexffmpeg/releases/latest'
$url = ($rel.assets | Where-Object name -Like '*essentials_build.zip').browser_download_url
$zip = Join-Path $env:TEMP 'ffmpeg-essentials.zip'
Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::OpenRead($zip)
$entry = $z.Entries | Where-Object FullName -Match 'bin/ffprobe\.exe$' | Select-Object -First 1
New-Item -ItemType Directory -Force VlcFolderQueue/tools | Out-Null
[System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "VlcFolderQueue/tools/ffprobe.exe", $true)
$z.Dispose()
```

Without it, files are added to the library but never get a known duration,
so they're excluded from queue generation until ffprobe is present.

## VLC requirement

Needs VLC media player installed (auto-detected via registry or the default
`Program Files` install paths). If it can't be found, the app shows an error
instead of crashing.
