$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path (Split-Path $PSScriptRoot)
$script = Join-Path $root 'publish-store.ps1'
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) "EpubMerge-StoreValidation-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
$settingsPath = Join-Path $temporaryDirectory 'settings.json'
$settings = Get-Content -Raw -LiteralPath (Join-Path $root 'packaging\store-settings.json') | ConvertFrom-Json
$settings.IdentityName = ''
$settings.Publisher = ''
$settings.PublisherDisplayName = ''
$settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8

function Assert-Fails {
    param([hashtable]$Arguments, [string]$Expected)
    try { & $script @Arguments }
    catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw }
        Write-Host "PASS: $Expected"
        return
    }
    throw "Expected a failure containing: $Expected"
}

try {
    Assert-Fails @{ PackageVersion = '1.2.3.0'; SettingsPath = $settingsPath } 'Set IdentityName'
    foreach ($version in @('1.2.3', '1.2.3.1', '01.2.3.0', '1.2.3.0\escape', 'V1.2.3')) {
        Assert-Fails @{ PackageVersion = $version; Demo = $true } 'PackageVersion must be'
    }
    foreach ($version in @('0.2.3.0', '1.65536.3.0')) {
        Assert-Fails @{ PackageVersion = $version; Demo = $true } 'MSIX version components'
    }
    $settings.IdentityName = 'EpubMerge.WinUI.Demo'
    $settings.Publisher = 'CN=EpubMerge.WinUI.Demo'
    $settings.PublisherDisplayName = 'Demo'
    $settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8
    Assert-Fails @{ PackageVersion = '1.2.3.0'; SettingsPath = $settingsPath } 'Invalid or demo Store identity'
    Write-Host 'Store configuration negative tests passed; no packages were built.'
}
finally {
    Remove-Item -LiteralPath $settingsPath -Force
    Remove-Item -LiteralPath $temporaryDirectory -Force
}
