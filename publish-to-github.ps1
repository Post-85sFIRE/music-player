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
    $repoSlug = "Post-85sFIRE/$RepoName"
    $repoUrl  = "https://github.com/$repoSlug.git"
    # If the repo already exists on GitHub, attach it as 'origin'; otherwise create it.
    # NOTE: `gh repo create` has NO `--yes` flag (that is `gh repo delete`). Do not add one.
    & gh repo view $repoSlug *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "GitHub repo '$repoSlug' already exists; attaching remote 'origin'." -ForegroundColor Cyan
        git remote add origin $repoUrl
    } else {
        Write-Host "Creating GitHub repo '$repoSlug'..." -ForegroundColor Cyan
        gh repo create $RepoName --$Visibility `
            --source . --remote origin `
            --description "Cross-platform music player (Windows WPF + .NET 10 + BASS / Android + Jetpack Compose + ExoPlayer), Nutstore WebDAV cloud playback"
        if ($LASTEXITCODE -ne 0) {
            Write-Host "gh repo create failed; attaching existing remote as fallback." -ForegroundColor Yellow
            git remote add origin $repoUrl
        }
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
# Make sure the release tag exists AND points at the commit we are releasing (HEAD).
$existingTags = Invoke-Git @('tag', '-l', $Version)
$headCommit   = (Invoke-Git @('rev-parse', 'HEAD') | Select-Object -First 1)
if (-not $existingTags) {
    Invoke-Git @('tag', $Version) | Out-Null
    Write-Host "Created tag $Version at $headCommit." -ForegroundColor Cyan
} else {
    $tagCommit = (Invoke-Git @('rev-parse', "$Version^{commit}") | Select-Object -First 1)
    if ($tagCommit -ne $headCommit) {
        Write-Host "Tag $Version pointed at stale commit $tagCommit; re-pointing to HEAD ($headCommit)." -ForegroundColor Yellow
        Invoke-Git @('tag', '-f', $Version) | Out-Null
    } else {
        Write-Host "Tag $Version already at HEAD ($headCommit)." -ForegroundColor DarkGray
    }
}

# Sync with remote so we only ever do a fast-forward push (never rewind main).
Invoke-Git @('fetch', 'origin') | Out-Null

Write-Host "Pushing main -> origin ..." -ForegroundColor Cyan
$pushMain = Invoke-Git @('push', '-u', 'origin', 'main')
$pushMain | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
if ($script:LastGitExit -ne 0) {
    Write-Error "git push main failed (exit $($script:LastGitExit)). If it asked for credentials, run:  gh auth setup-git  then re-run."
    exit 1
}

Write-Host "Pushing tag $Version -> origin ..." -ForegroundColor Cyan
$pushTag = Invoke-Git @('push', 'origin', "refs/tags/${Version}:refs/tags/${Version}")
$pushTag | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
if ($script:LastGitExit -ne 0) {
    Write-Error "git push tag $Version failed (exit $($script:LastGitExit))."
    exit 1
}
Write-Host "Push OK: main + tag $Version are on GitHub." -ForegroundColor Green

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


