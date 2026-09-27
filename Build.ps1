param([string]$ExecutableName='KoruGaming_Next.exe',[string]$Version,[string]$Repository,[switch]$UpdateTest,[string]$TestSource='')
$ErrorActionPreference='Stop'
$config=Get-Content (Join-Path $PSScriptRoot 'version.json') -Raw | ConvertFrom-Json
if(!$Version){$Version=$config.version};if(!$PSBoundParameters.ContainsKey('Repository')){$Repository=$config.githubRepository}
if($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$'){throw 'Invalid semantic version'}
if($Repository -and $Repository -notmatch '^https://github\.com/[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+$'){throw 'Invalid GitHub repository'}
$generated=Join-Path $PSScriptRoot '.generated';New-Item -ItemType Directory $generated -Force | Out-Null
$numeric=($Version -split '-')[0];$testFlag=([bool]$UpdateTest).ToString().ToLowerInvariant();$escapedSource=$TestSource.Replace('"','""')
@"
using System.Reflection;
[assembly: AssemblyVersion("$numeric.0")]
[assembly: AssemblyFileVersion("$numeric.0")]
[assembly: AssemblyInformationalVersion("$Version")]
internal static class UpdateBuild {internal const string Version="$Version",Repository="$Repository",TestSource=@"$escapedSource";internal const bool Testing=$testFlag;}
"@ | Set-Content (Join-Path $generated 'Version.cs') -Encoding utf8
$env:NYANG_BUILD_VERSION=$Version

Push-Location "$PSScriptRoot\frontend"
try { & node build.mjs; if($LASTEXITCODE -ne 0){throw 'Frontend build failed'} } finally {Pop-Location}
$sdk=Join-Path $PSScriptRoot 'vendor\webview2'
$out=Join-Path $PSScriptRoot 'dist'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 "/out:$out\$ExecutableName" "/win32manifest:$PSScriptRoot\host\app.manifest" "/win32icon:$PSScriptRoot\frontend\public\nyang.ico" "/reference:$env:WINDIR\System32\WinMetadata\Windows.UI.winmd" "/reference:$env:WINDIR\System32\WinMetadata\Windows.Data.winmd" "/reference:$env:WINDIR\System32\WinMetadata\Windows.Foundation.winmd" "/reference:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.dll" "/reference:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.Net.Http.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Web.dll "/reference:$PSScriptRoot\vendor\velopack\lib\net472\Velopack.dll" "/reference:$PSScriptRoot\vendor\newtonsoft\lib\net45\Newtonsoft.Json.dll" "/reference:$sdk\lib\net462\Microsoft.Web.WebView2.Core.dll" "/reference:$sdk\lib\net462\Microsoft.Web.WebView2.WinForms.dll" "$generated\Version.cs" "$PSScriptRoot\host\UpdateService.cs" "$PSScriptRoot\host\UpdateTests.cs" "$PSScriptRoot\host\Program.cs" "$PSScriptRoot\host\GamingService.cs" "$PSScriptRoot\host\HoYoLabProvider.cs" "$PSScriptRoot\host\HoyoLoginWindow.cs" "$PSScriptRoot\host\HoyoTests.cs" "$PSScriptRoot\host\GenshinCharacters.cs" "$PSScriptRoot\host\CharacterImageCache.cs" "$PSScriptRoot\host\GamingOverlay.cs" "$PSScriptRoot\host\EternalReturn.cs" "$PSScriptRoot\host\EternalReturnTests.cs" "$PSScriptRoot\host\EternalDakProvider.cs" "$PSScriptRoot\host\ProviderHttpPolicy.cs" "$PSScriptRoot\host\EternalHippyProvider.cs" "$PSScriptRoot\host\ProviderPolicyTests.cs" "$PSScriptRoot\host\EternalCraftProvider.cs" "$PSScriptRoot\host\EternalCraftTests.cs" "$PSScriptRoot\host\EternalDakTests.cs" "$PSScriptRoot\host\ChatNotifications.cs" "$PSScriptRoot\host\MessengerService.cs" "$PSScriptRoot\host\MessengerTests.cs" "$PSScriptRoot\host\FriendsService.cs" "$PSScriptRoot\host\FriendsTests.cs" "$PSScriptRoot\host\RiotService.cs" "$PSScriptRoot\host\ValorantService.cs" "$PSScriptRoot\host\ValorantTests.cs" "$PSScriptRoot\host\RiotTests.cs" "$PSScriptRoot\host\LeagueApi.cs" "$PSScriptRoot\host\LeagueService.cs" "$PSScriptRoot\host\LeagueIplolProvider.cs" "$PSScriptRoot\host\LeagueIplolTests.cs" "$PSScriptRoot\host\LeagueRankStore.cs" "$PSScriptRoot\host\LeagueTests.cs"
if($LASTEXITCODE -ne 0){throw 'Host build failed'}
foreach($dependency in @("$sdk\lib\net462\Microsoft.Web.WebView2.Core.dll","$sdk\lib\net462\Microsoft.Web.WebView2.WinForms.dll","$sdk\runtimes\win-x64\native\WebView2Loader.dll")){
    $destination=Join-Path $out (Split-Path $dependency -Leaf)
    if(!(Test-Path -LiteralPath $destination) -or (Get-FileHash -LiteralPath $dependency).Hash -ne (Get-FileHash -LiteralPath $destination).Hash){Copy-Item -LiteralPath $dependency -Destination $destination -Force}
}
Copy-Item "$PSScriptRoot\host\app.config" "$out\$ExecutableName.config" -Force
if(Test-Path -LiteralPath "$PSScriptRoot\friends-backend.json"){Copy-Item -LiteralPath "$PSScriptRoot\friends-backend.json" -Destination "$out\friends-backend.json" -Force}
Write-Output "Built $out\$ExecutableName"

Copy-Item "$PSScriptRoot/vendor/velopack/lib/net472/Velopack.dll" "$out/Velopack.dll" -Force
Copy-Item "$PSScriptRoot/vendor/newtonsoft/lib/net45/Newtonsoft.Json.dll" "$out/Newtonsoft.Json.dll" -Force
