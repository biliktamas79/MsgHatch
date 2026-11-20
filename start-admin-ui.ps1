#!/usr/bin/env pwsh
# Starts ExampleApp and then AdminUiExample, the two apps of a test of the admin UI, and waits until each answers on its
# port. An app that already answers stays as it is. Ctrl+C stops the apps that this script started. The output of each app
# goes to artifacts/admin-ui. The script checks that the token of AdminUiExample has the roles of the admin pages.
param([int]$TimeoutSeconds = 180)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$logs = Join-Path $repo 'artifacts/admin-ui'
$adminUrl = 'http://localhost:5090/'
$requiredRoles = 'TemplateAdmin', 'RegistryAdmin', 'DeliveryAdmin', 'DeliveryContent'
$tokenStep = 'Do step 1 of "Start the admin UI" in examples/ExampleApp/README.md, which stores a token with all roles.'
$started = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

function Test-Answer([string]$url) {
    try {
        $null = Invoke-WebRequest $url -SkipHttpErrorCheck -TimeoutSec 2
        $true
    }
    catch {
        $false
    }
}

# The JWT payload is base64url text, so it needs the standard alphabet and the padding before the decode.
function Get-TokenRole {
    $line = dotnet user-secrets list --project (Join-Path $repo 'examples/AdminUiExample') | Where-Object { $_ -like 'AdminUiExample:ApiToken = *' }
    if (-not $line) {
        throw "AdminUiExample has no token in its user secrets. $tokenStep"
    }

    try {
        $payload = ($line -split ' = ', 2)[1].Split('.')[1].Replace('-', '+').Replace('_', '/')
        $payload += '=' * ((4 - $payload.Length % 4) % 4)
        $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
        return @($claims.role)
    }
    catch {
        throw "The token of AdminUiExample is not a JWT. $tokenStep"
    }
}

function Start-App([string]$name, [string]$url) {
    if (Test-Answer $url) {
        Write-Host "$name already answers at $url."
        return
    }

    $project = Join-Path $repo "examples/$name"
    $process = Start-Process dotnet -ArgumentList 'run', '--project', $project -WorkingDirectory $project -NoNewWindow -PassThru `
        -RedirectStandardOutput (Join-Path $logs "$name.log") -RedirectStandardError (Join-Path $logs "$name.errors.log")
    $started.Add($process)
    Write-Host "$name builds and starts."
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Answer $url)) {
        if ($process.HasExited) {
            throw "$name stopped at its start. Read $(Join-Path $logs "$name.log")."
        }

        if ($clock.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
            throw "$name did not answer at $url in $TimeoutSeconds seconds. Read $(Join-Path $logs "$name.log")."
        }

        Start-Sleep -Seconds 1
    }

    Write-Host "$name answers at $url."
}

$tokenRoles = Get-TokenRole
$missingRoles = @($requiredRoles | Where-Object { $_ -notin $tokenRoles })
if ($missingRoles.Count -gt 0) {
    Write-Warning "The token of AdminUiExample has no role $($missingRoles -join ', '), so the pages that need it show `"You have no permission to do this action.`". $tokenStep"
}

New-Item -ItemType Directory -Force $logs | Out-Null
try {
    Start-App 'ExampleApp' 'http://localhost:5080/'
    Start-App 'AdminUiExample' $adminUrl
    if ($started.Count -eq 0) {
        Write-Host "Open $adminUrl."
        return
    }

    Write-Host "Open $adminUrl. Press Ctrl+C to stop the apps that this script started."
    while ($true) {
        if (@($started | Where-Object HasExited).Count -gt 0) {
            throw "An app that this script started stopped. Read the logs in $logs."
        }

        Start-Sleep -Seconds 1
    }
}
finally {
    foreach ($process in $started) {
        if (-not $process.HasExited) {
            $process.Kill($true)
        }
    }
}
