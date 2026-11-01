# Dot-source this before building. With a local .NET SDK in tools\dotnet (not in the repository) nothing is written
# outside VRMod\tools; without one, the dotnet on your PATH is used.
$VRModRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"
if (Test-Path (Join-Path $PSScriptRoot "dotnet")) {
    $env:DOTNET_ROOT = Join-Path $PSScriptRoot "dotnet"
    $env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot "home"
    $env:NUGET_PACKAGES = Join-Path $PSScriptRoot "nuget"
    $env:PATH = "$env:DOTNET_ROOT;$(Join-Path $PSScriptRoot 'ilspy');$env:PATH"
}
