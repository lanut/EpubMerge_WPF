[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [Parameter(Mandatory)]
    [string]$PackageVersion,

    [string]$SettingsPath = (Join-Path $PSScriptRoot 'packaging\store-settings.json'),

    [ValidateSet('Managed', 'NativeAot')]
    [string]$CompilationMode = 'Managed',

    [switch]$Demo
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PackageVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.0$') {
    throw 'PackageVersion must be Major.Minor.Build.0, without leading zeros (for example 1.2.3.0).'
}
$parts = $PackageVersion.Split('.') | ForEach-Object { [int]$_ }
if ($parts[0] -lt 1 -or ($parts | Where-Object { $_ -gt 65535 })) {
    throw 'MSIX version components must be in 0..65535 and Major must be positive.'
}

$settings = Get-Content -Raw -LiteralPath $SettingsPath | ConvertFrom-Json
if ($Demo) {
    $settings.IdentityName = 'EpubMerge.WinUI.Demo'
    $settings.Publisher = 'CN=EpubMerge.WinUI.Demo'
    $settings.PublisherDisplayName = 'EpubMerge Demo - NOT FOR STORE'
    $settings.DisplayName = 'EPUB Merger DEMO'
}
foreach ($field in @('IdentityName', 'Publisher', 'PublisherDisplayName', 'DisplayName', 'Description')) {
    if ([string]::IsNullOrWhiteSpace($settings.$field) -or
        ($field -in @('IdentityName', 'Publisher', 'PublisherDisplayName') -and
         $settings.$field -match '(?i)REQUIRED|PLACEHOLDER|YOUR[_ -]')) {
        throw "Set $field in $SettingsPath using Partner Center product identity. Use -Demo only for a test package."
    }
}
if ($settings.IdentityName -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{2,49}$' -or
    $settings.Publisher -notmatch '^CN=' -or
    (-not $Demo -and ($settings.IdentityName -match '(?i)\.Demo$' -or $settings.Publisher -match '(?i)EpubMerge\.WinUI\.Demo'))) {
    throw "Invalid or demo Store identity in $SettingsPath."
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio with MSBuild and WinUI tools is required.' }
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
    Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio MSBuild.exe was not found.' }
$nativeAot = $CompilationMode -eq 'NativeAot'
$dumpbin = $null
if ($nativeAot) {
    $dumpbin = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -find 'VC\Tools\MSVC\**\bin\Hostx64\x64\dumpbin.exe' | Select-Object -First 1
    if (-not $dumpbin) { throw 'NativeAot requires the MSVC C++ x64/x86 build tools (and ARM64 tools for win-arm64).' }
}
$sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\10.0.26100.0\x64'
$makeappx = Join-Path $sdkBin 'makeappx.exe'
if (-not (Test-Path -LiteralPath $makeappx)) { throw 'Install Windows SDK 26100 (including MakeAppx).' }

$platform = if ($RuntimeIdentifier -eq 'win-arm64') { 'ARM64' } else { 'x64' }
$mode = if ($Demo) { 'demo' } else { 'store' }
$output = Join-Path $PSScriptRoot "publish\msix\$mode\$PackageVersion\$CompilationMode\$RuntimeIdentifier"
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$work = Join-Path $output 'work'
New-Item -ItemType Directory -Path $work | Out-Null
$manifestPath = Join-Path $work 'Package.appxmanifest'
[xml]$manifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'src\EpubMerge.Gui.WinUI\Package.appxmanifest.template')
$manifest.Package.Identity.Name = $settings.IdentityName
$manifest.Package.Identity.Publisher = $settings.Publisher
$manifest.Package.Identity.Version = $PackageVersion
$manifest.Package.Properties.DisplayName = $settings.DisplayName
$manifest.Package.Properties.PublisherDisplayName = $settings.PublisherDisplayName
$manifest.Package.Properties.Description = $settings.Description
$visual = $manifest.Package.Applications.Application.VisualElements
$visual.SetAttribute('DisplayName', $settings.DisplayName)
$visual.SetAttribute('Description', $settings.Description)
$manifest.Save($manifestPath)

$arguments = @(
    (Join-Path $PSScriptRoot 'src\EpubMerge.Gui.WinUI\EpubMerge.Gui.WinUI.csproj'),
    '/restore', '/t:Rebuild', '/nologo', '/verbosity:minimal',
    '/p:Configuration=Release', "/p:Platform=$platform", "/p:RuntimeIdentifier=$RuntimeIdentifier",
    '/p:StorePackage=true', "/p:StoreManifestPath=$manifestPath",
    "/p:PublishAot=$($nativeAot.ToString().ToLowerInvariant())",
    "/p:AppxPackageDir=$output\",
    "/bl:$output\build.binlog",
    '/p:GenerateDocumentationFile=false', '/p:DebugSymbols=false', '/p:DebugType=None'
)
& $msbuild @arguments
if ($LASTEXITCODE -ne 0) { throw "Store MSBuild failed with exit code $LASTEXITCODE." }

$packages = @(Get-ChildItem -LiteralPath $output -Recurse -File -Filter '*.msix')
$mainPackages = @($packages | Where-Object { $_.Name -notmatch '(?i)Microsoft\.|_language-|_scale-' })
if ($mainPackages.Count -ne 1) { throw "Expected one main MSIX, found $($mainPackages.Count) in $output." }
$uploads = @(Get-ChildItem -LiteralPath $output -File -Filter '*.msixupload')
if ($uploads.Count -ne 1) {
    throw 'Expected one StoreUpload submission file.'
}
$submissionPackage = Join-Path $work 'submission.msix'
$archive = [IO.Compression.ZipFile]::OpenRead($uploads[0].FullName)
try {
    $entries = @($archive.Entries | Where-Object { $_.Name -like '*.msix' })
    if ($entries.Count -ne 1) { throw 'Expected one architecture-specific MSIX inside StoreUpload.' }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], $submissionPackage)
}
finally { $archive.Dispose() }
$unpacked = Join-Path $work 'unpacked'
& $makeappx unpack /p $submissionPackage /d $unpacked /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx could not validate/unpack the generated package.' }
[xml]$builtManifest = Get-Content -Raw -LiteralPath (Join-Path $unpacked 'AppxManifest.xml')
if ($builtManifest.Package.Identity.Name -ne $settings.IdentityName -or
    $builtManifest.Package.Identity.Publisher -ne $settings.Publisher -or
    $builtManifest.Package.Identity.Version -ne $PackageVersion -or
    $builtManifest.Package.Identity.ProcessorArchitecture -ne $platform.ToLowerInvariant()) {
    throw 'Built package identity, version or architecture did not match the request.'
}
$application = $builtManifest.Package.Applications.Application
if ($application.Executable -ne 'EpubMerge.Gui.WinUI.exe' -or
    $application.EntryPoint -ne 'Windows.FullTrustApplication') {
    throw 'Store package must launch the WinUI full-trust desktop executable.'
}
$requiredAssets = @('EpubMerge.Gui.WinUI.exe', 'resources.pri',
    'Assets\AppIcon.ico',
    'Assets\Store\StoreLogo.png', 'Assets\Store\Square44x44Logo.png', 'Assets\Store\Square150x150Logo.png')
$managedAssets = @('EpubMerge.Gui.WinUI.dll', 'EpubMerge.Presentation.dll',
    'EpubMerge.Core.Pure.dll', 'coreclr.dll', 'EpubMerge.Gui.WinUI.runtimeconfig.json')
$resourceDirectory = Join-Path $PSScriptRoot 'src\EpubMerge.Presentation\Resources'
$managedAssets += @(Get-ChildItem -LiteralPath $resourceDirectory -Filter 'Language.*.resx' -File | ForEach-Object {
    $culture = $_.Name.Substring('Language.'.Length, $_.Name.Length - 'Language.'.Length - '.resx'.Length)
    "$culture\EpubMerge.Presentation.resources.dll"
})
$expectedLanguages = @($manifest.Package.Resources.Resource | ForEach-Object { $_.Language } | Sort-Object)
$actualLanguages = @($builtManifest.Package.Resources.Resource | ForEach-Object { $_.Language } | Sort-Object)
if (Compare-Object $expectedLanguages $actualLanguages) {
    throw 'Built package languages do not match the Store manifest template.'
}
if (-not $nativeAot) { $requiredAssets += $managedAssets }
foreach ($asset in $requiredAssets) {
    if (-not (Test-Path -LiteralPath (Join-Path $unpacked $asset) -PathType Leaf)) {
        throw "Missing Store payload: $asset"
    }
}
$exeBytes = [IO.File]::ReadAllBytes((Join-Path $unpacked 'EpubMerge.Gui.WinUI.exe'))
$peOffset = [BitConverter]::ToInt32($exeBytes, 0x3c)
$machine = [BitConverter]::ToUInt16($exeBytes, $peOffset + 4)
$expectedMachine = if ($platform -eq 'ARM64') { 0xAA64 } else { 0x8664 }
if ($machine -ne $expectedMachine) { throw 'MSIX executable architecture does not match the manifest.' }
if (Test-Path -LiteralPath (Join-Path $unpacked 'Microsoft.UI.Xaml.dll')) {
    throw 'Store package unexpectedly embeds Windows App SDK instead of using the framework dependency.'
}
if ($nativeAot) {
    foreach ($asset in $managedAssets + @('hostfxr.dll', 'hostpolicy.dll', 'EpubMerge.Gui.WinUI.deps.json')) {
        if (Test-Path -LiteralPath (Join-Path $unpacked $asset)) {
            throw "NativeAot package contains managed runtime payload: $asset"
        }
    }
    $exports = & $dumpbin /exports (Join-Path $unpacked 'EpubMerge.Gui.WinUI.exe')
    if ($LASTEXITCODE -ne 0 -or -not ($exports -match '\bDotNetRuntimeDebugHeader\b')) {
        throw 'Packaged executable is not the expected Native AOT image (missing native runtime export).'
    }
    $exports | Set-Content -LiteralPath (Join-Path $output 'native-exports.txt')
}
else {
    $runtime = Get-Content -Raw -LiteralPath (Join-Path $unpacked 'EpubMerge.Gui.WinUI.runtimeconfig.json') | ConvertFrom-Json
    if ($runtime.runtimeOptions.PSObject.Properties['framework'] -or
        $runtime.runtimeOptions.PSObject.Properties['frameworks'] -or
        -not ($runtime.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' })) {
        throw 'Store runtime configuration must be self-contained for .NET.'
    }
}
$dependencies = @($builtManifest.Package.Dependencies.PackageDependency)
if (-not ($dependencies | Where-Object { $_.Name -like 'Microsoft.WindowsAppRuntime*' })) {
    throw 'Missing Windows App SDK framework dependency in the generated MSIX manifest.'
}
if (-not ($builtManifest.Package.Capabilities.Capability | Where-Object { $_.Name -eq 'runFullTrust' })) {
    throw 'Missing desktop runFullTrust capability.'
}
if (Get-ChildItem -LiteralPath $unpacked -Filter 'EpubMerge.Gui.exe') { throw 'WPF must not be in the Store package.' }
if (Get-ChildItem -LiteralPath $unpacked -Filter 'EpubMerge.Cli.exe') { throw 'CLI must not be in the Store package.' }
$report = [ordered]@{
    Mode = $mode
    Identity = $settings.IdentityName
    Publisher = $settings.Publisher
    Version = $PackageVersion
    Architecture = $platform
    CompilationMode = $CompilationMode
    DotNet = if ($nativeAot) { 'NativeAot' } else { 'SelfContained' }
    WindowsAppSDK = 'FrameworkDependent'
    Languages = $actualLanguages
    Dependencies = @($dependencies | ForEach-Object { "$($_.Name) >= $($_.MinVersion)" })
    Signed = $false
}
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'package-validation.json') -Encoding utf8
Remove-Item -LiteralPath $work -Recurse -Force
Write-Host "Validated UNSIGNED $mode package: $output"
if ($Demo) { Write-Warning 'DEMO identity: do not submit this package to Partner Center.' }
