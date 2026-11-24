#requires -Version 7.4
# Build, pack and install ContextGuru as a global tool. Nothing global is touched until the build and pack have passed.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

# Run from the repo root so global.json applies, and leave the caller's folder as it was.
Push-Location (Split-Path $PSScriptRoot)
try {
    & "$PSScriptRoot\build.ps1"

    # Install this exact version, so an older .nupkg left in the folder is never picked.
    $nupkg = Join-Path $PSScriptRoot 'nupkg'
    $version = (dotnet msbuild src/ContextGuru -getProperty:Version).Trim()
    dotnet pack src/ContextGuru -c Release -o $nupkg

    if (dotnet tool list -g | Where-Object { $_ -match '^ContextGuru\s' }) {
        dotnet tool uninstall -g ContextGuru
    }
    # --source, not --add-source: nuget.org must not be searched for this package id.
    dotnet tool install -g ContextGuru --version $version --source $nupkg

    contextguru --doctor

    Write-Host ''
    Write-Host 'Register the tool with each agent you use:'
    foreach ($agent in 'claude', 'codex', 'cursor') {
        Write-Host "  contextguru install --agent $agent"
    }
}
finally {
    Pop-Location
}
