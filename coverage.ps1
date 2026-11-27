#!/usr/bin/env pwsh
# Runs each unit test project with coverage, and writes the HTML report to artifacts/coverage.
# With -IncludeIntegration, it also runs each integration test project with a server set: its folder, or the folder of an
# integration test project that it references, has appsettings.Local.json. Such a project runs as dotnet test runs it.
# Fails when the line coverage of the production code is less than -MinimumLineCoverage percent.
param([double]$MinimumLineCoverage = 90, [switch]$IncludeIntegration)

$ErrorActionPreference = 'Stop'
$tests = Join-Path $PSScriptRoot 'tests'
$output = Join-Path $PSScriptRoot 'artifacts/coverage'
$raw = Join-Path $output 'raw'
Remove-Item $output -Recurse -Force -ErrorAction Ignore

function Test-ServerSetting([IO.FileInfo]$project) {
    $referenced = (Select-Xml -Path $project.FullName -XPath '//ProjectReference/@Include').Node.Value |
        Where-Object { $_ -like '*.Tests.Integration.csproj' } |
        ForEach-Object { Split-Path (Join-Path $project.DirectoryName $_) }
    return [bool](@($project.DirectoryName) + @($referenced) | Where-Object { Test-Path (Join-Path $_ 'appsettings.Local.json') })
}

dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$projects = @(Get-ChildItem $tests -Recurse -Filter '*.Tests.Unit.csproj')
if ($IncludeIntegration) {
    $integration = @(Get-ChildItem $tests -Recurse -Filter '*.Tests.Integration.csproj' | Where-Object { Test-ServerSetting $_ })
    Write-Host "Integration test projects with a server: $(($integration.BaseName | Sort-Object) -join ', ')"
    $projects += $integration
}

foreach ($project in $projects) {
    dotnet test $project.FullName --collect 'XPlat Code Coverage' --results-directory $raw
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

dotnet reportgenerator "-reports:$raw/*/coverage.cobertura.xml" "-targetdir:$output" '-reporttypes:Html;TextSummary;JsonSummary'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$lineCoverage = (Get-Content (Join-Path $output 'Summary.json') -Raw | ConvertFrom-Json).summary.linecoverage
Write-Host "Line coverage: $lineCoverage %. Report: $(Join-Path $output 'index.html')"
if ($lineCoverage -lt $MinimumLineCoverage) {
    Write-Error "The line coverage $lineCoverage % is less than $MinimumLineCoverage %."
    exit 1
}
