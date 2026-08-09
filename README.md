# VLC Folder Queue

Windows WPF app that builds a randomized, time-boxed watch queue from a
library of video folders and hands it to VLC as an `.m3u` playlist.

- **Library tab**: add folders, exclude subfolders, mark a folder "episodic"
  (its files queue in order as a block instead of being shuffled
  individually), tag folders freely.
- **History tab**: see what's already been played; mark items unplayed to
  put them back in rotation.
- **Queue Builder tab**: enter a target runtime in minutes, generate a
  randomized queue (episodic folders inserted as ordered blocks, everything
  else shuffled) within ±15% of the target, review/edit it, then send it to
  VLC.

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
