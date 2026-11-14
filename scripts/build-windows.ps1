$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$backend = Join-Path (Get-Location) 'vendor/rclone/rclone.exe'
if (!(Test-Path $backend)) {
    $stage = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $stage | Out-Null
    try {
        $zip = Join-Path $stage 'rclone.zip'
        Invoke-WebRequest 'https://downloads.rclone.org/v1.76.0/rclone-v1.76.0-windows-amd64.zip' -OutFile $zip
        if ((Get-FileHash $zip).Hash -ne '8b4fd2737ade4e70c057ef4a49deb7cffe94b0fb9d7587c189d149823f21a1dc') { throw 'rclone checksum mismatch' }
        Expand-Archive $zip -DestinationPath $stage
        New-Item -ItemType Directory -Force -Path (Split-Path $backend) | Out-Null
        Copy-Item (Join-Path $stage 'rclone-v1.76.0-windows-amd64/rclone.exe') $backend
    } finally { Remove-Item $stage -Recurse -Force }
}
dotnet build src/RCHub.Desktop/RCHub.Desktop.csproj -m:1
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project tests/RCHub.Checks
if ($LASTEXITCODE -ne 0) { throw 'Checks failed' }
dotnet build tests/RCHub.UiChecks -m:1
if ($LASTEXITCODE -ne 0) { throw 'UI build failed' }
$uiPreview = Join-Path ([IO.Path]::GetTempPath()) ("RCH-ui-" + [guid]::NewGuid().ToString() + ".png")
try {
    dotnet run --no-build --project tests/RCHub.UiChecks -- $uiPreview
    if ($LASTEXITCODE -ne 0) { throw 'UI interaction checks failed' }
} finally {
    if (Test-Path $uiPreview) { Remove-Item $uiPreview }
}
dotnet publish src/RCHub.Desktop/RCHub.Desktop.csproj -c Release -r win-x64 --self-contained true -m:1 -o release/RCH-win-x64
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

$probe = Start-Process -FilePath (Join-Path (Get-Location) 'release/RCH-win-x64/RCH.exe') -ArgumentList '--check-native-host' -PassThru
if (!$probe.WaitForExit(30000)) { $probe.Kill(); throw 'Native Windows child-window check timed out' }
if ($probe.ExitCode -ne 0) { throw "Native Windows child-window check failed: $($probe.ExitCode)" }
Write-Host 'PASS published Windows executable creates a native child window'
