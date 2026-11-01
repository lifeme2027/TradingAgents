$ErrorActionPreference = "Stop"
$sdk = "C:\Users\ADMINI~1\AppData\Local\Temp\2\dotnet\dotnet.exe"
if (-not (Test-Path $sdk)) {
    $sdk = "dotnet"
}
$env:DOTNET_ROOT = Split-Path $sdk -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
Set-Location $PSScriptRoot
& $sdk build "$PSScriptRoot\TradingAgents.sln" -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $sdk test "$PSScriptRoot\TradingAgents.sln" -c Release --no-build
exit $LASTEXITCODE
