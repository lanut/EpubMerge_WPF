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

$guiProjectPath = Join-Path $PSScriptRoot 'src\EpubMerge.Gui\EpubMerge.Gui.csproj'
$cliProjectPath = Join-Path $PSScriptRoot 'src\EpubMerge.Cli\EpubMerge.Cli.csproj'

foreach ($projectPath in @($guiProjectPath, $cliProjectPath)) {
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

    if ($Clean -and (Test-Path -LiteralPath $targetPublishDirectory)) {
        Remove-Item -LiteralPath $targetPublishDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $targetPublishDirectory -Force | Out-Null

    foreach ($project in @(
        @{ Path = $guiProjectPath; Name = 'EpubMerge.Gui' },
        @{ Path = $cliProjectPath; Name = 'EpubMerge.Cli' }
    )) {
        $publishArguments = @(
            'publish',
            $project.Path,
            '--configuration', $Configuration,
            '--runtime', $TargetRuntimeIdentifier,
            '--self-contained', $targetSelfContainedValue,
            '--output', $targetPublishDirectory,
                '-p:PublishSingleFile=true'
        )

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
    }

    Write-Host "Published GUI and CLI to: $targetPublishDirectory"
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