#!/usr/bin/env pwsh
# Runs each unit test project with coverage, and writes the HTML report to artifacts/coverage.
# Fails when the line coverage of the production code is less than -MinimumLineCoverage percent.
param([double]$MinimumLineCoverage = 90)

$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'artifacts/coverage'
$raw = Join-Path $output 'raw'
Remove-Item $output -Recurse -Force -ErrorAction Ignore

dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

foreach ($project in Get-ChildItem (Join-Path $PSScriptRoot 'tests') -Recurse -Filter '*.Tests.Unit.csproj') {
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
