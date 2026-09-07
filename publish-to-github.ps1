# One-click publish to GitHub.
# Run on YOUR machine where `gh` is already logged in (gh auth login).
# Usage:  .\publish-to-github.ps1  [-RepoName music-player] [-Visibility public] [-Version v0.1.0]
param(
    [string]$RepoName = "music-player",
    [string]$Visibility = "public",   # public | private
    [string]$Version   = "v0.1.0"
)
# 'Stop' is fine for our own Write-Error calls, but external git/gh commands
# must NOT abort on stderr (Windows PowerShell 5.1 re-promotes stderr to
# terminating errors even with 2>$null). Use a flag + try/catch instead.
$script:Strict = $true

# ---- Tool checks -----------------------------------------------------------
# PowerShell sometimes does NOT inherit git/gh PATH (e.g. shortcut-launched
# consoles, fresh install before reopen). Try to self-heal before failing.

function Test-Command($name) {
    $null -ne (Get-Command $name -ErrorAction SilentlyContinue)
}

if (-not (Test-Command git)) {
    # PowerShell sometimes inherits a partial PATH (shortcut-launched consoles,
    # fresh installs). Re-merge the full Machine+User PATH from the registry
    # and try common Git for Windows install locations before giving up.

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
    # Last-resort: ask cmd.exe (its PATH lookup is more permissive).
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

Manual fallback (no install needed):
    1) Open a new PowerShell window
    2) Run:    where.exe git
    3) Copy the printed path's parent directory, then in this same window:
       `$env:Path = 'PASTE_PARENT_DIR;' + `$env:Path
       Example: `$env:Path = 'C:\Program Files\Git\bin;' + `$env:Path
    4) Re-run:  .\publish-to-github.cmd

If `where.exe git` returned nothing, install Git for Windows:
    https://git-scm.com/download/win
    (check 'Add Git to PATH' during install), then REOPEN PowerShell.
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

# Verify gh is logged in. `gh auth status` writes to stderr on success
# ("You are logged into github.com..."), and to stdout on failure, so we
# just look at the exit code.
& gh auth status 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error @"
gh is not logged in.

Run:  gh auth login
Then:  .\publish-to-github.cmd
"@
    exit 1
}

# ---- Pre-flight ------------------------------------------------------------

$zip = "publish\win-x64\MusicPlayer-Desktop-win-x64-$Version.zip"
if (-not (Test-Path $zip)) {
    Write-Error "Cannot find $zip. Build & zip first (see README)."
    exit 1
}

# ---- Init / remote / commit / push / release (idempotent) -------------------

# Native-command helper. Always captures $LASTEXITCODE without letting stderr
# escalate into a terminating error (Windows PowerShell 5.1 quirk).
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
# Detect via `git remote` exit code rather than `git remote get-url origin`
# (the latter prints to stderr when origin doesn't exist and trips Stop).
$remoteList = Invoke-Git @('remote')
$hasOrigin  = ($script:LastGitExit -eq 0) -and (($remoteList -split "`r?`n") -contains 'origin')
if (-not $hasOrigin) {
    Write-Host "No remote 'origin' found, creating GitHub repo '$RepoName'..." -ForegroundColor Cyan
    gh repo create $RepoName --$Visibility --source . --remote origin `
        --description "Cross-platform music player (Windows WPF + .NET 10 + BASS)" --yes
}

# Stage + commit if dirty.
Invoke-Git @('add', '-A') | Out-Null
$porcelain = Invoke-Git @('status', '--porcelain')
if ($porcelain) {
    Invoke-Git @('commit', '-m', "Release $Version") | Out-Null
}
$existingTags = Invoke-Git @('tag', '-l', $Version)
if (-not $existingTags) {
    Invoke-Git @('tag', $Version) | Out-Null
}
Invoke-Git @('push', '-u', 'origin', 'main') | Out-Null
Invoke-Git @('push', '--tags') | Out-Null

gh release create $Version $zip --title "MusicPlayer $Version" --notes-file RELEASE_NOTES.md

$user = gh api user --jq .login
Write-Host ""
Write-Host ("Published {0} -> https://github.com/{1}/{2}/releases/tag/{0}" -f $Version, $user, $RepoName) -ForegroundColor Green
