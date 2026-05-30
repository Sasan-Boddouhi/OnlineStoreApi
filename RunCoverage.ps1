# RunCoverage.ps1

Write-Host "Cleaning old coverage data..." -ForegroundColor Cyan
Get-ChildItem -Recurse -Directory -Filter "TestResults" |
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Running tests with coverage..." -ForegroundColor Cyan
dotnet test --collect:"XPlat Code Coverage"

Write-Host "Generating report..." -ForegroundColor Cyan
reportgenerator `
    -reports:"**/coverage.cobertura.xml" `
    -targetdir:"coveragereport" `
    -reporttypes:Html

Write-Host "Coverage report generated." -ForegroundColor Green
start coveragereport/index.html 