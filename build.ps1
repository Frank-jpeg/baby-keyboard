param(
    [string]$SdkPath = '',
    [switch]$NativeTests
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = $PSScriptRoot
if (-not $SdkPath) {
    $localSdk = Join-Path (Split-Path $projectRoot -Parent) 'tools\dotnet10\dotnet.exe'
    if (Test-Path -LiteralPath $localSdk) { $SdkPath = $localSdk }
    else { $SdkPath = (Get-Command dotnet -ErrorAction Stop).Source }
}
$SdkPath = (Resolve-Path -LiteralPath $SdkPath).Path
$env:DOTNET_ROOT = Split-Path $SdkPath -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.packages'
$appProject = Join-Path $projectRoot 'src\BabyKeyboard.App\BabyKeyboard.App.csproj'
$testProject = Join-Path $projectRoot 'tests\BabyKeyboard.Tests\BabyKeyboard.Tests.csproj'
$testExe = Join-Path $projectRoot 'tests\BabyKeyboard.Tests\bin\Release\net10.0-windows\BabyKeyboard.Tests.exe'
$releaseDir = Join-Path $projectRoot 'artifacts\release'
$publishDir = Join-Path $projectRoot 'artifacts\publish'
[xml]$metadata = Get-Content -LiteralPath $appProject -Encoding UTF8 -Raw
$version = [string]$metadata.Project.PropertyGroup.Version
$title = [string]$metadata.Project.PropertyGroup.AssemblyTitle
$releaseExe = Join-Path $releaseDir ($title + '-v' + $version + '.exe')
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

& $SdkPath build $testProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
& $SdkPath publish $appProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $publishDir 'BabyKeyboard.exe') -Destination $releaseExe -Force
$usageSource = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs') -Filter '*.txt' | Select-Object -First 1
$usageTarget = Join-Path $releaseDir $usageSource.Name
Copy-Item -LiteralPath $usageSource.FullName -Destination $usageTarget -Force

$notices = [System.Collections.Generic.List[string]]::new()
foreach ($package in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
    $packageRoot = Join-Path $env:NUGET_PACKAGES $package
    $packageVersion = Get-ChildItem -LiteralPath $packageRoot -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    foreach ($noticeFile in (Get-ChildItem -LiteralPath $packageVersion.FullName -File | Where-Object { $_.Name -match 'LICENSE|THIRD-PARTY' })) {
        $notices.Add($package + ' ' + $packageVersion.Name + ' / ' + $noticeFile.Name)
        $notices.Add([IO.File]::ReadAllText($noticeFile.FullName, [Text.Encoding]::UTF8))
    }
}
$noticesPath = Join-Path $releaseDir 'RUNTIME-NOTICES.txt'
[IO.File]::WriteAllText($noticesPath, ($notices -join ([Environment]::NewLine + [Environment]::NewLine)), [Text.UTF8Encoding]::new($false))

$previewPath = Join-Path $releaseDir 'preview.png'
$previewInfo = [System.Diagnostics.ProcessStartInfo]::new($releaseExe)
$previewInfo.UseShellExecute = $false
$previewInfo.CreateNoWindow = $true
$previewInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
# Windows PowerShell 5.1 does not expose ArgumentList; the two fixed arguments are safely quoted here.
$previewInfo.Arguments = '--render-preview "' + $previewPath + '"'
$previewProcess = [System.Diagnostics.Process]::Start($previewInfo)
if (-not $previewProcess.WaitForExit(20000)) {
    $previewProcess.Kill()
    throw 'Preview render timed out.'
}
if ($previewProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $previewPath)) { throw 'Preview render failed.' }
$previewProcess.Dispose()

if ($NativeTests) {
    & $testExe --integration $releaseExe
    if ($LASTEXITCODE -ne 0) { throw 'Native integration tests failed.' }
}
$archivePath = Join-Path $releaseDir ($title + '-v' + $version + '-win-x64.zip')
Compress-Archive -LiteralPath @($releaseExe, $usageTarget, $previewPath, $noticesPath) -DestinationPath $archivePath -Force
$hash = (Get-FileHash -LiteralPath $releaseExe -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $releaseDir 'SHA256.txt'), $hash + '  ' + [IO.Path]::GetFileName($releaseExe) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Output $releaseExe
Write-Output $archivePath
