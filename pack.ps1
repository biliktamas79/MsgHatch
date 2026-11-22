#!/usr/bin/env pwsh
# Packs each packable project in Release into artifacts/packages, and checks each package.
# Fails when a package has a wrong version or wrong metadata, when a package is missing,
# or when a package has no project under src/.
param([string]$VersionSuffix = '')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$output = Join-Path $PSScriptRoot 'artifacts/packages'
Remove-Item $output -Recurse -Force -ErrorAction Ignore

function Get-VersionPrefix {
    $props = [xml](Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw)
    @($props.Project.PropertyGroup | ForEach-Object { $_.VersionPrefix } | Where-Object { $_ })[0]
}

function Get-ExpectedPackageIds {
    Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter '*.csproj' -Recurse |
        Where-Object { (Get-Content $_.FullName -Raw) -notmatch '<IsPackable>false</IsPackable>' } |
        ForEach-Object { $_.BaseName } | Sort-Object
}

function Read-PackageEntry([System.IO.Compression.ZipArchive]$zip, [string]$path) {
    $reader = [System.IO.StreamReader]::new($zip.GetEntry($path).Open())
    try { $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Get-SourceFiles([string]$folder, [string]$filter) {
    Get-ChildItem $folder -Filter $filter -Recurse -File -ErrorAction Ignore |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
}

function Get-StaticWebAssetFailures([string]$id, [string[]]$entries) {
    $project = Join-Path $PSScriptRoot "src/$id"
    $webRoot = Join-Path $project 'wwwroot'
    foreach ($file in Get-SourceFiles $webRoot '*') {
        $path = 'staticwebassets/' + [System.IO.Path]::GetRelativePath($webRoot, $file.FullName).Replace('\', '/')
        if ($entries -notcontains $path) { "$id has no $path." }
    }
    if (@(Get-SourceFiles $project '*.razor.css').Count -gt 0 -and -not ($entries -like 'staticwebassets/*.bundle.scp.css')) {
        "$id has no bundle of its scoped CSS."
    }
}

function Get-PackageFailures([string]$file, [string]$expectedVersion) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName })
        $nuspec = [xml](Read-PackageEntry $zip ($entries | Where-Object { $_ -like '*.nuspec' }))
    }
    finally { $zip.Dispose() }

    $metadata = $nuspec.package.metadata
    $id = $metadata.id
    if ($metadata.version -ne $expectedVersion) { "$id has the version $($metadata.version), not $expectedVersion." }
    if ($metadata.license.type -ne 'expression' -or $metadata.license.InnerText -ne 'Apache-2.0') { "$id has no license expression Apache-2.0." }
    if ([string]::IsNullOrWhiteSpace($metadata.description) -or $metadata.description -eq 'Package Description') { "$id has no description." }
    if ([string]::IsNullOrWhiteSpace($metadata.tags)) { "$id has no tags." }
    if ($metadata.readme -ne 'README.md' -or $entries -notcontains 'README.md') { "$id has no README." }
    if (-not $metadata.repository -or [string]::IsNullOrWhiteSpace($metadata.repository.commit)) { "$id has no repository commit." }
    if (-not (Test-Path (Join-Path $output "$id.$expectedVersion.snupkg"))) { "$id has no symbol package." }

    foreach ($dependency in $nuspec.SelectNodes("//*[local-name()='dependency']")) {
        if ($dependency.id -eq 'MsgHatch.SourceGeneration') { "$id depends on MsgHatch.SourceGeneration." }
        elseif ($dependency.id -like 'MsgHatch.*' -and $dependency.version -ne $expectedVersion) { "$id depends on $($dependency.id) $($dependency.version), not $expectedVersion." }
    }

    if ($id -eq 'MsgHatch.Templates' -and $entries -notcontains 'analyzers/dotnet/cs/MsgHatch.SourceGeneration.dll') { "$id has no generator in analyzers/dotnet/cs." }
    Get-StaticWebAssetFailures $id $entries
}

$versionPrefix = Get-VersionPrefix
$expectedVersion = if ($VersionSuffix) { "$versionPrefix-$VersionSuffix" } else { $versionPrefix }
$packArguments = @('pack', (Join-Path $PSScriptRoot 'MsgHatch.slnx'), '-c', 'Release', '-o', $output)
if ($VersionSuffix) { $packArguments += @('--version-suffix', $VersionSuffix) }
dotnet @packArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$packages = @(Get-ChildItem $output -Filter '*.nupkg')
$expectedIds = @(Get-ExpectedPackageIds)
$actualIds = @($packages | ForEach-Object { $_.Name -replace "\.$([regex]::Escape($expectedVersion))\.nupkg$", '' } | Sort-Object)
$failures = @(
    $expectedIds | Where-Object { $actualIds -notcontains $_ } | ForEach-Object { "$_ has no package." }
    $actualIds | Where-Object { $expectedIds -notcontains $_ } | ForEach-Object { "$_ has no packable project under src/." }
    $packages | ForEach-Object { Get-PackageFailures $_.FullName $expectedVersion }
)

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" }
    Write-Error "$($failures.Count) package checks failed."
    exit 1
}
Write-Host "$($packages.Count) packages of version $expectedVersion pass the checks. Folder: $output"
