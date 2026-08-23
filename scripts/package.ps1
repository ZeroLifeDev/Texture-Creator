param([string]$Version = "v0.3.0-alpha")
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$out = Join-Path $root "artifacts\publish\win-x64"
$zip = Join-Path $root "artifacts\PBR-Reference-Forge-$Version-win-x64.zip"
$standaloneExe = Join-Path $root "artifacts\PBR-Reference-Forge-$Version-win-x64.exe"
dotnet publish (Join-Path $root "src\TextureCreator.App\TextureCreator.App.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
Copy-Item (Join-Path $root "README.md") $out
$resolvedOut = (Resolve-Path $out).Path
if (-not $resolvedOut.StartsWith($root.Path, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Publish directory escaped the repository." }
Get-ChildItem -LiteralPath $resolvedOut -Filter "*.pdb" -File | Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $resolvedOut "PBRReferenceForge.exe") -Destination $standaloneExe -Force
if (Test-Path $zip) { Remove-Item -LiteralPath $zip }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -CompressionLevel Optimal
Write-Output $standaloneExe
Write-Output $zip
