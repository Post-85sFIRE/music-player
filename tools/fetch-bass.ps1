# fetch-bass.ps1
# Download x64 native BASS DLLs into the exe output dir (required before M1 can produce sound).
# Pulls bass24 + WASAPI add-on + common decoder add-ons from un4seen.com and extracts the x64 bass*.dll files:
#   bass.dll (core: MP3/WAV/OGG)  basswasapi.dll (exclusive output)  bassflac.dll  bassopus.dll  bassape.dll  bassalac.dll
# Note: AAC (bass_aac) is a commercial add-on and is NOT downloaded here; OGG is supported by bass.dll natively.
# Your machine must be able to reach https://www.un4seen.com (the dev sandbox blocks outbound to it; run this on your own Windows).
#
# How to run:
#   cd "D:\我的文件\Documents\WorkBuddy\音乐播放器" && .\tools\fetch-bass.ps1
#   OR from anywhere: powershell -ExecutionPolicy Bypass -File "D:\我的文件\Documents\WorkBuddy\音乐播放器\tools\fetch-bass.ps1"
param(
    [string]$Config = 'Debug',
    [string]$Tfm = 'net10.0-windows10.0.19041.0'
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Path $PSScriptRoot -Parent
$staging = Join-Path $repo 'src/MusicPlayer.Audio/Native/x64'
$out = Join-Path $repo ('src/MusicPlayer.Desktop/bin/' + $Config + '/' + $Tfm)

New-Item -ItemType Directory -Force -Path $staging | Out-Null
New-Item -ItemType Directory -Force -Path $out | Out-Null

$urls = @(
    'https://www.un4seen.com/files/bass24.zip',
    'https://www.un4seen.com/files/basswasapi24.zip',
    'https://www.un4seen.com/files/bassflac24.zip',
    'https://www.un4seen.com/files/bassopus24.zip',
    'https://www.un4seen.com/files/bassape24.zip',
    'https://www.un4seen.com/files/bassalac24.zip'
)

Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($url in $urls) {
    $name = [System.IO.Path]::GetFileName($url)
    $tmp = Join-Path $env:TEMP $name
    Write-Host ('Downloading ' + $url)
    Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing

    $zip = [System.IO.Compression.ZipFile]::OpenRead($tmp)
    try {
        foreach ($entry in $zip.Entries) {
            if (($entry.FullName -like 'x64/*') -and ($entry.Name -like '*.dll')) {
                $destStaging = Join-Path $staging $entry.Name
                $destOut = Join-Path $out $entry.Name
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destStaging, $true)
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destOut, $true)
                Write-Host ('   -> ' + $entry.Name)
            }
        }
    } finally {
        $zip.Dispose()
    }
    Remove-Item $tmp -Force
}

Write-Host ''
Write-Host 'Done. Native BASS DLLs placed at:'
Write-Host ('  staging (auto-copied on build): ' + $staging)
Write-Host ('  output  (run directly):         ' + $out)
Write-Host 'You can now run MusicPlayer.Desktop.exe to verify playback.'
