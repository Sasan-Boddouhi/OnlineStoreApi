# RunCoverage.ps1

Write-Host "Cleaning old build artifacts and coverage data..." -ForegroundColor Cyan
Get-ChildItem -Recurse -Directory -Filter "bin" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Recurse -Directory -Filter "obj" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Recurse -Directory -Filter "TestResults" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Recurse -Filter "coverage.cobertura.xml" | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "Running tests with coverage..." -ForegroundColor Cyan
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings

Write-Host "Generating report..." -ForegroundColor Cyan
reportgenerator `
    -reports:"**/coverage.cobertura.xml" `
    -targetdir:"coveragereport" `
    -reporttypes:Html `
    -verbosity:Warning

Write-Host "Coverage report generated." -ForegroundColor Green
start coveragereport/index.html