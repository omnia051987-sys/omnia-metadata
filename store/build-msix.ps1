param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$AllowPlaceholderIdentity
)

$ErrorActionPreference = 'Stop'
$storeRoot = (Resolve-Path $PSScriptRoot).Path
$repoRoot = (Resolve-Path (Join-Path $storeRoot '..')).Path
$manifest = Join-Path $storeRoot 'Package.appxmanifest'
$sourceIcon = Join-Path $repoRoot 'assets\omnia-icon.png'
$publish = Join-Path $repoRoot 'outputs\Omnia.Metadata.App.publish'
$stage = Join-Path $repoRoot 'outputs\OmniaMetadata.Store'
$package = Join-Path $repoRoot 'outputs\OmniaMetadata-0.7.3.msix'

if (-not $AllowPlaceholderIdentity -and (Select-String -LiteralPath $manifest -Pattern 'Publisher="CN=Omnia Metadata"' -Quiet)) {
    throw 'A identidade/publisher ainda é provisória. Substitua pelos valores atribuídos pelo Partner Center ou use -AllowPlaceholderIdentity apenas para validação local.'
}

if (-not (Test-Path (Join-Path $publish 'Omnia.Metadata.App.exe'))) {
    dotnet publish (Join-Path $repoRoot 'src\Omnia.Metadata.App\Omnia.Metadata.App.csproj') `
        -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=false -o $publish
}

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage, (Join-Path $stage 'Assets') -Force | Out-Null
Get-ChildItem -LiteralPath $publish -Force | Copy-Item -Destination $stage -Recurse -Force
Copy-Item -LiteralPath $manifest -Destination (Join-Path $stage 'AppxManifest.xml') -Force

foreach ($asset in @('StoreLogo.png', 'Square44x44Logo.png', 'Square150x150Logo.png')) {
    Copy-Item -LiteralPath $sourceIcon -Destination (Join-Path $stage "Assets\$asset") -Force
}

$makeAppx = Get-Command makeappx.exe -ErrorAction SilentlyContinue
if ($null -eq $makeAppx) {
    $makeAppx = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\makeappx.exe', 'C:\Program Files\Windows Kits\10\bin\*\x64\makeappx.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
}
if ($null -eq $makeAppx) {
    throw 'makeappx.exe não foi encontrado. Instale o Windows SDK ou use o ambiente de empacotamento do Visual Studio para gerar o MSIX.'
}

if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package -Force }
$makeAppxPath = if ($makeAppx.PSObject.Properties.Name -contains 'Source' -and $makeAppx.Source) { $makeAppx.Source } else { $makeAppx.FullName }
& $makeAppxPath pack /d $stage /p $package /o
if ($LASTEXITCODE -ne 0) { throw "makeappx.exe terminou com código $LASTEXITCODE." }
Write-Host "MSIX criado em $package"
