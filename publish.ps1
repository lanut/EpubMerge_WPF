[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [ValidateSet('SelfContained', 'FrameworkDependent')]
    [string]$DeploymentMode = 'SelfContained',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Clean,

    [switch]$All
)

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'src\EpubMerge.Gui\EpubMerge.Gui.csproj'

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Project file was not found: $projectPath"
}

function Publish-Target {
    param(
        [string]$TargetRuntimeIdentifier,
        [string]$TargetDeploymentMode
    )

    $targetModeDirectory = if ($TargetDeploymentMode -eq 'SelfContained') { 'self-contained' } else { 'framework-dependent' }
    $targetPublishDirectory = Join-Path $PSScriptRoot "publish\$TargetRuntimeIdentifier\$targetModeDirectory"
    $targetSelfContainedValue = if ($TargetDeploymentMode -eq 'SelfContained') { 'true' } else { 'false' }

    if ($Clean -and (Test-Path -LiteralPath $targetPublishDirectory)) {
        Remove-Item -LiteralPath $targetPublishDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $targetPublishDirectory -Force | Out-Null

    $publishArguments = @(
        'publish',
        $projectPath,
        '--configuration', $Configuration,
        '--runtime', $TargetRuntimeIdentifier,
        '--self-contained', $targetSelfContainedValue,
        '--output', $targetPublishDirectory,
        '-p:PublishSingleFile=true'
    )

    Write-Host "Publishing EpubMerge.Gui [$TargetRuntimeIdentifier, $targetModeDirectory, $Configuration]..."
    & dotnet @publishArguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    Write-Host "Published to: $targetPublishDirectory"
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