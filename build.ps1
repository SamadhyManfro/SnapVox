# Builds SnapVox for release: native (AOT) publish, the Inno Setup installer and the portable exe.
# Usage:  .\build.ps1            (version from SnapVox.csproj)
#         .\build.ps1 -Version 0.2.0
# Output: artifacts\SnapVox-Setup-<version>.exe, its .sig and artifacts\SnapVox-Portable-<version>.exe
#         (all three are attached to the GitHub release)

param([string]$Version)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\SnapVox\SnapVox.csproj'
$publish = Join-Path $root 'artifacts\publish'

if (-not $Version) {
    $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
Write-Host "SnapVox $Version" -ForegroundColor Cyan

# 1) Native publish (needs the C++ tools from the Visual Studio Build Tools).
#    The native compiler finds Visual Studio through vswhere.exe, which must be on the PATH.
$vsInstaller = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if ((Test-Path $vsInstaller) -and ($env:Path -notlike "*$vsInstaller*")) { $env:Path = "$vsInstaller;$env:Path" }
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Release -r win-x64 -o $publish -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
# Debug symbols of the native libraries (~100 MB) don't go into the installer.
Get-ChildItem $publish -Filter *.pdb -Recurse | Remove-Item -Force

# 2) Installer.
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup)." }

& $iscc "/DAppVersion=$Version" "/DPublishDir=$publish" (Join-Path $root 'installer\SnapVox.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

$setup = Join-Path $root "artifacts\SnapVox-Setup-$Version.exe"

# 3) Signature for automatic updates. The private key stays outside the repository.
$signature = "$setup.sig"
if (Test-Path $signature) { Remove-Item $signature -Force }
$keyFile = Join-Path $env:USERPROFILE '.snapvox\update-signing-key.pem'
if (Test-Path $keyFile) {
    $hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    $message = [Text.Encoding]::UTF8.GetBytes("SnapVox-update-v1`n$Version`n$hash`n")
    $key = [Security.Cryptography.ECDsa]::Create()
    try {
        $key.ImportFromPem([IO.File]::ReadAllText($keyFile))
        $signed = $key.SignData($message, [Security.Cryptography.HashAlgorithmName]::SHA256)
        [IO.File]::WriteAllText($signature, [Convert]::ToBase64String($signed))
    } finally { $key.Dispose() }
} else {
    Write-Warning "No signing key at ${keyFile}: this installer can't be delivered through automatic updates."
}

# 4) Portable: a single exe with the native libraries (and their hashes) embedded.
$payload = Join-Path $root 'artifacts\portable-payload.zip'
$payloadDir = Join-Path $root 'artifacts\portable-payload'
$portableBuild = Join-Path $root 'artifacts\portable-build'
$portable = Join-Path $root "artifacts\SnapVox-Portable-$Version.exe"
foreach ($old in $payload, $payloadDir, $portableBuild) { if (Test-Path $old) { Remove-Item $old -Recurse -Force } }
New-Item -ItemType Directory $payloadDir | Out-Null
Copy-Item (Join-Path $publish '*.dll') $payloadDir
$hashes = Get-ChildItem $payloadDir -Filter *.dll | Sort-Object Name | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) $($_.Name)"
}
[IO.File]::WriteAllText((Join-Path $payloadDir 'portable-hashes.txt'), (($hashes -join "`n") + "`n"))
Compress-Archive -Path (Join-Path $payloadDir '*') -DestinationPath $payload -CompressionLevel Optimal
dotnet publish $project -c Release -r win-x64 -o $portableBuild -p:Version=$Version "-p:PortablePayload=$payload"
if ($LASTEXITCODE -ne 0) { throw "Portable publish failed." }
Copy-Item (Join-Path $portableBuild 'SnapVox.exe') $portable -Force
Remove-Item $portableBuild, $payload, $payloadDir -Recurse -Force

foreach ($file in $setup, $portable) {
    Write-Host "Done: $file ($([math]::Round((Get-Item $file).Length / 1MB, 1)) MB)" -ForegroundColor Green
}
if (Test-Path $signature) { Write-Host "Signed: $signature (attach it to the release too)" -ForegroundColor Green }
