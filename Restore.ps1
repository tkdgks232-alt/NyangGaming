param([string]$Npm='npm')
$ErrorActionPreference='Stop'
$packages=@(
 @{Id='velopack';Version='1.2.158';Folder='velopack';Sha256='EB25B2E137DA1EEAAB74053ED9BDC7EF261CAE5B151582E8D6878EE004FB2A14';Check='lib/net472/Velopack.dll'},
 @{Id='newtonsoft.json';Version='13.0.4';Folder='newtonsoft';Sha256='F09081D457405BAF35A973FA0C50D6BF272ED683F2568C5A620A49DA952F6529';Check='lib/net45/Newtonsoft.Json.dll'},
 @{Id='microsoft.web.webview2';Version='1.0.2903.40';Folder='webview2';Sha256='EF128016DD1E51C59178C827ED5B8AA3322C57AFA8675D930F8109505542AD74';Check='lib/net462/Microsoft.Web.WebView2.Core.dll'}
)
foreach($p in $packages){
 $dest=Join-Path $PSScriptRoot ('vendor/'+$p.Folder)
 if(Test-Path (Join-Path $dest $p.Check)){continue}
 $cache=Join-Path $PSScriptRoot '.packages';New-Item -ItemType Directory $cache -Force | Out-Null
 $base="https://api.nuget.org/v3-flatcontainer/$($p.Id)/$($p.Version)/$($p.Id).$($p.Version).nupkg"
 $zip=Join-Path $cache ($p.Id+'.zip');Invoke-WebRequest $base -OutFile $zip
 if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $p.Sha256){throw "NuGet checksum mismatch: $($p.Id)"}
 Expand-Archive -LiteralPath $zip -DestinationPath $dest -Force
}
Push-Location (Join-Path $PSScriptRoot 'frontend')
try{if($Npm.EndsWith('.js')){node $Npm ci --ignore-scripts --no-audit --no-fund}else{& $Npm ci --ignore-scripts --no-audit --no-fund};if($LASTEXITCODE){throw 'Frontend dependency restore failed'}}finally{Pop-Location}
