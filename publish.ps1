[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [ValidateSet('SelfContained', 'FrameworkDependent')]
    [string]$DeploymentMode = 'SelfContained',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$ExcludeXmlAndPdb,

    [switch]$Clean,

    [switch]$All
)

$ErrorActionPreference = 'Stop'

$wpfProjectPath = Join-Path $PSScriptRoot 'src\EpubMerge.Gui\EpubMerge.Gui.csproj'
$winuiProjectPath = Join-Path $PSScriptRoot 'src\EpubMerge.Gui.WinUI\EpubMerge.Gui.WinUI.csproj'
$cliProjectPath = Join-Path $PSScriptRoot 'src\EpubMerge.Cli\EpubMerge.Cli.csproj'

foreach ($projectPath in @($wpfProjectPath, $winuiProjectPath, $cliProjectPath)) {
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Project file was not found: $projectPath"
    }
}

function Publish-Target {
    param(
        [string]$TargetRuntimeIdentifier,
        [string]$TargetDeploymentMode
    )

    $targetModeDirectory = if ($TargetDeploymentMode -eq 'SelfContained') { 'self-contained' } else { 'framework-dependent' }
    $targetPublishDirectory = Join-Path $PSScriptRoot "publish\$TargetRuntimeIdentifier\$targetModeDirectory"
    $targetSelfContainedValue = if ($TargetDeploymentMode -eq 'SelfContained') { 'true' } else { 'false' }
    $targetPlatform = if ($TargetRuntimeIdentifier -eq 'win-arm64') { 'ARM64' } else { 'x64' }

    if ($Clean -and (Test-Path -LiteralPath $targetPublishDirectory)) {
        Remove-Item -LiteralPath $targetPublishDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $targetPublishDirectory -Force | Out-Null

    foreach ($project in @(
        @{ Path = $wpfProjectPath; Name = 'EpubMerge.Gui'; Folder = 'wpf' },
        @{ Path = $winuiProjectPath; Name = 'EpubMerge.Gui.WinUI'; Folder = 'winui' },
        @{ Path = $cliProjectPath; Name = 'EpubMerge.Cli'; Folder = 'cli' }
    )) {
        $publishArguments = @(
            'publish',
            $project.Path,
            '--configuration', $Configuration,
            '--runtime', $TargetRuntimeIdentifier,
            '--self-contained', $targetSelfContainedValue,
            '--output', (Join-Path $targetPublishDirectory $project.Folder)
        )

        if ($project.Folder -eq 'winui') {
            $publishArguments += @(
                "-p:Platform=$targetPlatform",
                '-p:PublishSingleFile=false',
                "-p:WindowsAppSDKSelfContained=$targetSelfContainedValue",
                '-p:PublishTrimmed=false'
            )
        }
        else {
            $publishArguments += '-p:PublishSingleFile=true'
        }

        if ($ExcludeXmlAndPdb) {
            $publishArguments += @(
                '-p:GenerateDocumentationFile=false',
                '-p:DebugSymbols=false',
                '-p:DebugType=None',
                '-p:CopyOutputSymbolsToPublishDirectory=false'
            )
        }

        Write-Host "Publishing $($project.Name) [$TargetRuntimeIdentifier, $targetModeDirectory, $Configuration]..."
        & dotnet @publishArguments

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for $($project.Name) with exit code $LASTEXITCODE."
        }
        $executable = Join-Path $targetPublishDirectory "$($project.Folder)\$($project.Name).exe"
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
            throw "Published executable is missing: $executable"
        }
    }

    foreach ($asset in @('winui\Assets\AppIcon.ico', 'winui\EpubMerge.Gui.WinUI.pri', 'winui\EpubMerge.Gui.WinUI.dll', 'winui\EpubMerge.Presentation.dll', 'winui\en\EpubMerge.Presentation.resources.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $targetPublishDirectory $asset) -PathType Leaf)) {
            throw "Published GUI dependency is missing: $asset"
        }
    }
    if ($TargetDeploymentMode -eq 'SelfContained') {
        foreach ($asset in @('winui\coreclr.dll', 'winui\Microsoft.UI.Xaml.dll', 'wpf\PresentationNative_cor3.dll')) {
            if (-not (Test-Path -LiteralPath (Join-Path $targetPublishDirectory $asset) -PathType Leaf)) {
                throw "Self-contained runtime dependency is missing: $asset"
            }
        }
    }
    Write-Host "Published separate WPF, WinUI and CLI directories to: $targetPublishDirectory"
}

if ($All) {
    foreach ($targetRuntimeIdentifier in @('win-x64', 'win-arm64')) {
        foreach ($targetDeploymentMode in @('SelfContained', 'FrameworkDependent')) {
            Publish-Target $targetRuntimeIdentifier $targetDeploymentMode
        }
    }
}
else {
    Publish-Target $RuntimeIdentifier $DeploymentMode
}