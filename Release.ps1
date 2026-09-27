param([Parameter(Mandatory=$true)][string]$Version,[Parameter(Mandatory=$true)][string]$NotesFile)
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
if($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'){throw 'Use a stable semantic version, e.g. 1.0.1'}
$root=& git rev-parse --show-toplevel
if($LASTEXITCODE -or [IO.Path]::GetFullPath($root) -ne [IO.Path]::GetFullPath($PSScriptRoot)){throw 'Run from the dedicated Nyang Gaming git repository'}
if((& git status --porcelain)){throw 'Commit your code changes first. This script commits only version and release notes.'}
$notes=Get-Content -LiteralPath $NotesFile -Raw
if([string]::IsNullOrWhiteSpace($notes)){throw 'Release notes are empty'}
$cfg=Get-Content version.json -Raw | ConvertFrom-Json
if([version]$Version -le [version]$cfg.version){throw 'New version must be greater than current version'}
$remote=& git remote get-url origin
if($LASTEXITCODE -or $remote -notmatch '^(https://github\.com/|git@github\.com:)([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(\.git)?$'){throw 'Configure a GitHub origin first'}
$cfg.githubRepository='https://github.com/'+$Matches[2];$cfg.version=$Version
$cfg | ConvertTo-Json | Set-Content version.json -Encoding utf8
$notes | Set-Content ("release-notes/$Version.md") -Encoding utf8
git add -- version.json "release-notes/$Version.md"
if($LASTEXITCODE){throw 'git add failed'}
git commit -m "Release v$Version"
if($LASTEXITCODE){throw 'git commit failed'}
git tag -a "v$Version" -m "Nyang Gaming v$Version"
if($LASTEXITCODE){throw 'tag failed'}
git push --atomic origin HEAD "refs/tags/v$Version"
if($LASTEXITCODE){throw 'Push failed. Local release commit/tag are retained; no force-push was attempted.'}
Write-Output "Open $($cfg.githubRepository)/actions to follow the release build."
