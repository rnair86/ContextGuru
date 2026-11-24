#requires -Version 7.4
# Restore, build, test and format check. Stops at the first failing step.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

# Run from the repo root so global.json applies, and leave the caller's folder as it was.
Push-Location -LiteralPath (Split-Path -LiteralPath $PSScriptRoot)
try {
    dotnet restore ContextGuru.slnx
    dotnet build ContextGuru.slnx -c Release
    dotnet test ContextGuru.slnx -c Release --no-build
    dotnet format ContextGuru.slnx --verify-no-changes
}
finally {
    Pop-Location
}
