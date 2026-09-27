param([string]$Version,[string]$Repository,[string]$Vpk='vpk',[switch]$UpdateTest,[string]$TestSource='',[string]$OutputDir)
$ErrorActionPreference='Stop'
$cfg=Get-Content (Join-Path $PSScriptRoot 'version.json') -Raw | ConvertFrom-Json
if(!$Version){$Version=$cfg.version};if(!$PSBoundParameters.ContainsKey('Repository')){$Repository=$cfg.githubRepository}
if(!$UpdateTest -and !$Repository){throw 'Set githubRepository in version.json before creating the distributable installer.'}
if(!$OutputDir){$OutputDir=Join-Path $PSScriptRoot 'Releases'}
& (Join-Path $PSScriptRoot 'Build.ps1') -ExecutableName 'NyangGaming.exe' -Version $Version -Repository $Repository -UpdateTest:$UpdateTest -TestSource $TestSource
$stage=Join-Path $PSScriptRoot ('.packages/stage-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $stage -Force | Out-Null
# Explicit runtime allowlist: never copy UserData, DPAPI keys, development fixtures or SQL/test outputs.
foreach($file in @('NyangGaming.exe','NyangGaming.exe.config','Velopack.dll','Newtonsoft.Json.dll','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','friends-backend.json')){
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('dist/'+$file)) -Destination (Join-Path $stage $file)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dist/ui') -Destination $stage -Recurse
$id=if($UpdateTest){'NyangGaming.UpdateTest'}else{'NyangGaming'}
$title=if($UpdateTest){'Nyang Gaming Update Test'}else{'Nyang Gaming'}
$aumid=if($UpdateTest){'NyangGaming.UpdateTest'}else{'NyangGaming.Messenger'}
$notes=Join-Path $PSScriptRoot ('release-notes/'+$Version+'.md');if(!(Test-Path $notes)){throw "Missing release notes: $notes"}
& $Vpk pack --packId $id --packVersion $Version --packDir $stage --mainExe NyangGaming.exe --packTitle $title --packAuthors NyangGaming --channel win --runtime win-x64 --framework 'net48,webview2' --icon (Join-Path $PSScriptRoot 'frontend/public/nyang.ico') --releaseNotes $notes --outputDir $OutputDir --noPortable --delta None --aumid $aumid --shortcuts StartMenuRoot
if($LASTEXITCODE){throw 'Velopack packaging failed'}
Write-Output "Release package: $OutputDir"
