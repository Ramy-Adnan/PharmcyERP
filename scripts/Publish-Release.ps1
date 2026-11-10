#requires -Version 5.1
<#
.SYNOPSIS
Builds and verifies the complete customer application folder for Advanced Installer.
.EXAMPLE
.\scripts\Publish-Release.ps1 -Version 1.1.0 -LicensePublicKeyPath C:\VendorKeys\pharmacy-public.pem
.NOTES
Run on the developer machine with the .NET SDK. Private signing keys, customer
licenses and databases never belong in this folder. This does not build Setup.exe.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $LicensePublicKeyPath,

    [string] $OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-DotNet {
    param([string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE. The release was not completed."
    }
}

function Assert-File {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required release file is missing: $Path"
    }
}

function Assert-X64Pe {
    param([string] $Path)
    Assert-File $Path
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4d -or $bytes[1] -ne 0x5a) {
        throw "Not a Windows PE executable: $Path"
    }
    $offset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ($offset -lt 0 -or $offset -gt ($bytes.Length - 6) -or
        [BitConverter]::ToUInt32($bytes, $offset) -ne 0x00004550 -or
        [BitConverter]::ToUInt16($bytes, $offset + 4) -ne 0x8664) {
        throw "The Windows binary is not x64: $Path"
    }
}

function Assert-NoConfiguredSecrets {
    param($Node)
    if ($null -eq $Node -or $Node -is [string] -or $Node -is [ValueType]) { return }
    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [pscustomobject]) {
        foreach ($entry in $Node) { Assert-NoConfiguredSecrets $entry }
        return
    }
    foreach ($property in $Node.PSObject.Properties) {
        if ($property.Name -match '(?i)(password|api.?key|signing.?secret|private.?key)' -and
            -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw "appsettings.json contains a configured secret field: $($property.Name)"
        }
        Assert-NoConfiguredSecrets $property.Value
    }
}

$stagingDirectory = $null
try {
    $parsedVersion = [Version]::Parse($Version)
    # Windows Installer ProductVersion has three numeric fields. Keep .NET's
    # generated AssemblyVersion below its reserved UInt16.MaxValue as well.
    if ($parsedVersion.Major -gt 255 -or $parsedVersion.Minor -gt 255 -or $parsedVersion.Build -gt 65534) {
        throw 'Use major/minor 0-255 and build 0-65534 for an MSI-compatible version.'
    }

    $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $project = Join-Path $repoRoot 'src/PharmacyERP.WPF/PharmacyERP.WPF.csproj'
    $issuerProject = Join-Path $repoRoot 'tools/LicenseIssuer/LicenseIssuer.csproj'
    $publicKey = (Resolve-Path -LiteralPath $LicensePublicKeyPath -ErrorAction Stop).ProviderPath
    Assert-File $publicKey
    if ((Get-Item -LiteralPath $publicKey).Length -gt 65536) {
        throw 'The public PEM key must be smaller than 64 KB.'
    }
    if ($publicKey -match '[%;]') {
        throw 'Choose a public key path without percent or semicolon characters for MSBuild.'
    }
    $publicText = [System.IO.File]::ReadAllText($publicKey)
    if ($publicText -match '(?i)PRIVATE KEY' -or
        $publicText -notmatch '-----BEGIN (RSA )?PUBLIC KEY-----') {
        throw 'Supply only the public PEM key. Never supply the private signing key.'
    }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Install the .NET SDK on the developer machine before publishing.'
    }
    Invoke-DotNet -Arguments @('run', '--project', $issuerProject, '-c', 'Release', '--',
        'validate-public-key', '--public-key', $publicKey)

    if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
        $OutputRoot = Join-Path $repoRoot 'artifacts/release'
    }
    $OutputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot)
    $sourceDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'src'))
    $comparison = [StringComparison]::OrdinalIgnoreCase
    if ($OutputRoot.Equals($sourceDirectory, $comparison) -or
        $OutputRoot.StartsWith($sourceDirectory + [System.IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw 'Choose an output directory outside src so published files cannot become project inputs.'
    }
    $releaseDirectory = Join-Path $OutputRoot $Version
    if (Test-Path -LiteralPath $releaseDirectory) {
        throw "Release $Version already exists. Use a new version or a different OutputRoot; nothing was overwritten."
    }
    [void][System.IO.Directory]::CreateDirectory($OutputRoot)
    $stagingDirectory = Join-Path $OutputRoot ('.staging-' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
    $appDirectory = Join-Path $stagingDirectory 'app'
    [void][System.IO.Directory]::CreateDirectory($appDirectory)

    Invoke-DotNet -Arguments @('publish', $project, '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'true', '-p:EnableWindowsTargeting=true',
        '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:PublishReadyToRun=false',
        '-p:PlatformTarget=x64', '-p:DebugType=None', '-p:DebugSymbols=false',
        "-p:Version=$Version", "-p:AssemblyVersion=$Version.0", "-p:FileVersion=$Version.0",
        "-p:LicensePublicKeyFile=$publicKey", '-o', $appDirectory)

    foreach ($file in @('PharmacyERP.WPF.exe', 'PharmacyERP.WPF.dll', 'PharmacyERP.Infrastructure.dll',
        'PharmacyERP.Application.dll', 'PharmacyERP.Domain.dll', 'PharmacyERP.WPF.runtimeconfig.json',
        'PharmacyERP.WPF.deps.json', 'appsettings.json', 'coreclr.dll', 'hostfxr.dll',
        'System.Private.CoreLib.dll', 'PresentationFramework.dll', 'PresentationCore.dll',
        'tessdata/ara.traineddata', 'tessdata/eng.traineddata', 'Resources/Fonts/Cairo-OFL.txt',
        'Resources/ThirdParty/ZXing.Net.LICENSE.txt')) {
        Assert-File (Join-Path $appDirectory $file)
    }
    Assert-X64Pe (Join-Path $appDirectory 'PharmacyERP.WPF.exe')
    Assert-X64Pe (Join-Path $appDirectory 'coreclr.dll')
    $nativeOcrDirectory = Join-Path $appDirectory 'x64'
    if (-not (Test-Path -LiteralPath $nativeOcrDirectory -PathType Container)) {
        throw 'The x64 OCR native directory is missing.'
    }
    foreach ($nativeName in @('*tesseract*.dll', '*leptonica*.dll')) {
        $matches = @(Get-ChildItem -LiteralPath $nativeOcrDirectory -File -Filter $nativeName)
        if ($matches.Count -eq 0) { throw "Required x64 OCR library is missing: $nativeName" }
        foreach ($native in $matches) { Assert-X64Pe $native.FullName }
    }
    $sqlNatives = @(Get-ChildItem -LiteralPath $appDirectory -Recurse -File -Filter 'Microsoft.Data.SqlClient.SNI.dll' |
        Where-Object { $_.FullName -notmatch '[/\\](x86|win-x86)[/\\]' })
    if ($sqlNatives.Count -eq 0) { throw 'The SQL Server x64 native client is missing.' }
    foreach ($native in $sqlNatives) { Assert-X64Pe $native.FullName }

    $runtime = Get-Content -LiteralPath (Join-Path $appDirectory 'PharmacyERP.WPF.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (-not ($runtime.runtimeOptions.PSObject.Properties.Name -contains 'includedFrameworks')) {
        throw 'The published runtime configuration is not self-contained.'
    }
    $frameworkNames = @($runtime.runtimeOptions.includedFrameworks | ForEach-Object { $_.name })
    if ($frameworkNames -notcontains 'Microsoft.NETCore.App' -or $frameworkNames -notcontains 'Microsoft.WindowsDesktop.App') {
        throw 'Both .NET and Windows Desktop runtimes must be included.'
    }

    $settings = Get-Content -LiteralPath (Join-Path $appDirectory 'appsettings.json') -Raw | ConvertFrom-Json
    if (-not [string]::IsNullOrWhiteSpace([string]$settings.ConnectionStrings.DefaultConnection)) {
        throw 'Do not ship a developer/customer database connection in appsettings.json.'
    }
    Assert-NoConfiguredSecrets $settings
    $files = @(Get-ChildItem -LiteralPath $appDirectory -Recurse -File)
    foreach ($file in $files) {
        if ($file.Extension -match '^(?i:\.pem|\.key|\.pfx|\.p12|\.lic|\.bak|\.mdf|\.ldf|\.db|\.sqlite\d*)$' -or
            $file.Name -match '^(?i:machine\.id|receipt-settings\.json|connection-settings\.dat|invoice-ai-settings\.dat|\.env(?:\..*)?|appsettings\.(?:Local|Development)\.json)$' -or
            $file.Name -like '*LicenseIssuer*') {
            throw "Private/customer/developer file found in customer output: $($file.Name)"
        }
        if ($file.Extension -in @('.json', '.xml', '.config', '.txt') -and
            [System.IO.File]::ReadAllText($file.FullName) -match '-----BEGIN (?:ENCRYPTED |RSA |EC )?PRIVATE KEY-----') {
            throw "Private key material found in customer output: $($file.Name)"
        }
    }
    Invoke-DotNet -Arguments @('run', '--project', $issuerProject, '-c', 'Release', '--',
        'validate-release-key', '--assembly', (Join-Path $appDirectory 'PharmacyERP.Infrastructure.dll'),
        '--public-key', $publicKey)

    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $appDirectory 'PharmacyERP.WPF.dll'))
    if ($versionInfo.FileVersion -ne "$Version.0") {
        throw "The app file version does not match the requested release: $($versionInfo.FileVersion)"
    }

    $commit = $null
    $dirty = $null
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $gitCommit = & git -C $repoRoot rev-parse HEAD 2>$null
        if ($LASTEXITCODE -eq 0) {
            $commit = [string]$gitCommit
            $gitStatus = @(& git -C $repoRoot status --porcelain 2>$null)
            if ($LASTEXITCODE -eq 0) { $dirty = $gitStatus.Count -gt 0 }
        }
    }
    $hashes = @($files | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            Path = $_.FullName.Substring($appDirectory.Length + 1).Replace('\', '/')
            Length = $_.Length
            Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    $manifest = [ordered]@{
        Product = 'PharmacyERP'
        Version = $Version
        RuntimeIdentifier = 'win-x64'
        SelfContained = $true
        PublishedAtUtc = [DateTime]::UtcNow.ToString('o')
        GitCommit = $commit
        WorkingTreeHadChanges = $dirty
        PublicKeyPemSha256 = (Get-FileHash -LiteralPath $publicKey -Algorithm SHA256).Hash.ToLowerInvariant()
        Files = $hashes
    }
    [System.IO.File]::WriteAllText((Join-Path $stagingDirectory 'release-manifest.json'),
        ($manifest | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.Directory]::Move($stagingDirectory, $releaseDirectory)
    $stagingDirectory = $null
    Write-Host "Verified application folder: $(Join-Path $releaseDirectory 'app')"
    Write-Host "Release hashes: $(Join-Path $releaseDirectory 'release-manifest.json')"
    Write-Host 'Import the entire app folder into Advanced Installer. Build and test Setup.exe on Windows.'
}
catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    if ($stagingDirectory -and (Test-Path -LiteralPath $stagingDirectory)) {
        Write-Warning "Incomplete output was retained for diagnosis: $stagingDirectory"
    }
    exit 1
}
