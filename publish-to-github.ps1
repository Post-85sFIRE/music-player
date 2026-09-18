# One-click publish to GitHub.
# Run on YOUR machine where `gh` is already logged in (gh auth login).
# Usage:  .\publish-to-github.ps1  [-RepoName music-player] [-Visibility public] [-Version v1.0.0]
param(
    [string]$RepoName = "music-player",
    [string]$Visibility = "public",   # public | private
    [string]$Version   = "v1.0.0"
)
# 'Stop' is fine for our own Write-Error calls, but external git/gh commands
# must NOT abort on stderr. Use a flag + try/catch instead.
$script:Strict = $true

# ---- Tool checks -----------------------------------------------------------
function Test-Command($name) {
    $null -ne (Get-Command $name -ErrorAction SilentlyContinue)
}

if (-not (Test-Command git)) {
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath    = [Environment]::GetEnvironmentVariable("Path", "User")
    $fullPath    = "$machinePath$([System.IO.Path]::PathSeparator)$userPath"
    foreach ($p in $fullPath.Split([System.IO.Path]::PathSeparator)) {
        if ($p -and (Test-Path (Join-Path $p "git.exe"))) {
            $env:Path = "$p$([System.IO.Path]::PathSeparator)$env:Path"
            break
        }
    }
}
if (-not (Test-Command git)) {
    $candidates = @(
        (Join-Path $env:ProgramFiles "Git\bin"),
        (Join-Path $env:ProgramFiles "Git\cmd"),
        (Join-Path ${env:ProgramFiles(x86)} "Git\bin"),
        (Join-Path $env:LocalAppData "Programs\Git\bin"),
        "C:\Program Files\Git\bin",
        "C:\Program Files (x86)\Git\bin"
    )
    foreach ($p in $candidates) {
        if ($p -and (Test-Path (Join-Path $p "git.exe"))) {
            $env:Path = "$p$([System.IO.Path]::PathSeparator)$env:Path"
            break
        }
    }
}
if (-not (Test-Command git)) {
    $whereOut = cmd /c "where git" 2>$null
    if ($LASTEXITCODE -eq 0 -and $whereOut) {
        $gitExe = ($whereOut -split "`r?`n")[0].Trim()
        if ($gitExe -and (Test-Path $gitExe)) {
            $gitDir = Split-Path $gitExe -Parent
            $env:Path = "$gitDir$([System.IO.Path]::PathSeparator)$env:Path"
        }
    }
}
if (-not (Test-Command git)) {
    Write-Error @"
git still not found.

Run:  where.exe git   then add its parent dir to PATH, or install Git for Windows:
    https://git-scm.com/download/win  (check 'Add Git to PATH'), REOPEN PowerShell.
"@
    exit 1
}
if (-not (Test-Command gh)) {
    Write-Error @"
gh (GitHub CLI) not found in PATH.

Fix: install from https://cli.github.com/ (winget install GitHub.cli),
     log in with `gh auth login`, REOPEN PowerShell, re-run this script.
"@
    exit 1
}
Write-Host ("git = {0}" -f (Get-Command git).Source) -ForegroundColor DarkGray
Write-Host ("gh  = {0}" -f (Get-Command gh).Source)  -ForegroundColor DarkGray

& gh auth status 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error @"
gh is not logged in.

Run:  gh auth login
Then (recommended, so `git push` reuses the token):  gh auth setup-git
Then re-run:  .\publish-to-github.cmd
"@
    exit 1
}

# ---- Pre-flight: the two release binaries must exist on disk -----------------
$zip = "publish\win-x64\MusicPlayer-Desktop-win-x64-$Version.zip"
$apk = "release\app-yunMusicPlayer-release.apk"
if (-not (Test-Path $zip)) {
    Write-Error "Cannot find $zip. Build & zip the Windows package first."
    exit 1
}
if (-not (Test-Path $apk)) {
    Write-Error "Cannot find $apk. Place the Android release APK at that path first."
    exit 1
}

# ---- Init / remote / commit / push / release (idempotent) -------------------

function Invoke-Git([string[]]$Arguments) {
    $output = & git @Arguments 2>&1
    $script:LastGitExit = $LASTEXITCODE
    return $output
}

# init git if needed
if (-not (Test-Path ".git")) {
    Invoke-Git @('init', '--initial-branch=main') | Out-Null
}

# create remote repo on GitHub if not present.
$remoteList = Invoke-Git @('remote')
$hasOrigin  = ($script:LastGitExit -eq 0) -and (($remoteList -split "`r?`n") -contains 'origin')
if (-not $hasOrigin) {
    Write-Host "No remote 'origin' found, creating GitHub repo '$RepoName'..." -ForegroundColor Cyan
    gh repo create $RepoName --$Visibility `
        --source . --remote origin `
        --description "跨平台音乐播放器（Windows WPF + .NET 10 + BASS / Android + Jetpack Compose + ExoPlayer），坚果云 WebDAV 云播放" `
        --yes
    if ($LASTEXITCODE -ne 0) {
        # Likely 'repository already exists' (e.g. the app's update endpoint already
        # points at this repo). Attach the existing repo as origin and continue.
        Write-Host "gh repo create failed; assuming '$RepoName' already exists, attaching remote." -ForegroundColor Yellow
        git remote add origin "https://github.com/Post-85sFIRE/$RepoName.git"
    }
}

# Stage ONLY the Windows source + tooling + publish scripts + root metadata.
# (android/ harmony/ .workbuddy/ gradle-8.9/ release/ publish/ and all temp
#  logs are excluded via .gitignore; we add an explicit whitelist so a public
#  repo can never accidentally leak secrets or 100s of MB of build cruft.)
$whitelist = @(
    'src',
    'tools',
    'README.md',
    'RELEASE_NOTES.md',
    'version.json',
    '.gitignore',
    'publish-to-github.ps1',
    'publish-to-github.cmd'
)
Invoke-Git (@('add') + $whitelist) | Out-Null

$porcelain = Invoke-Git @('status', '--porcelain')
if ($porcelain) {
    Invoke-Git @('commit', '-m', "Release $Version") | Out-Null
} else {
    Write-Host "Working tree already committed for $Version (nothing new to commit)." -ForegroundColor DarkGray
}
$existingTags = Invoke-Git @('tag', '-l', $Version)
if (-not $existingTags) {
    Invoke-Git @('tag', $Version) | Out-Null
}
Invoke-Git @('push', '-u', 'origin', 'main') | Out-Null
Invoke-Git @('push', '--tags') | Out-Null

# Create the GitHub Release (idempotent: skip if it already exists).
$relView = gh release view $Version 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Release $Version already exists; uploading/refreshing assets..." -ForegroundColor DarkGray
    gh release upload $Version $zip $apk --clobber
} else {
    gh release create $Version $zip $apk `
        --title "MusicPlayer $Version" `
        --notes-file RELEASE_NOTES.md
}

$user = gh api user --jq .login
Write-Host ""
Write-Host ("Published {0} -> https://github.com/{1}/{2}/releases/tag/{0}" -f $Version, $user, $RepoName) -ForegroundColor Green
