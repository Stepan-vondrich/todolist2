# Build frontend + publish backend as a single self-contained Windows exe
# Run from the repo root:  .\build.ps1

$ErrorActionPreference = "Stop"

Write-Host "=== Building frontend ===" -ForegroundColor Cyan
Set-Location frontend
npm install
npm run build
Set-Location ..

Write-Host "=== Copying frontend build to backend wwwroot ===" -ForegroundColor Cyan
$wwwroot = "backend\TodoApi\wwwroot"
if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
Copy-Item "frontend\dist" $wwwroot -Recurse

Write-Host "=== Publishing backend as single exe ===" -ForegroundColor Cyan
dotnet publish backend\TodoApi\TodoApi.csproj `
  -r win-x64 `
  --self-contained `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -c Release `
  -o publish

Write-Host ""
Write-Host "=== Done! ===" -ForegroundColor Green
Write-Host "Executable: publish\TodoApi.exe"
Write-Host "Run it and open http://localhost:6001 in your browser."
Write-Host "Data (todos.db, uploads/) will be stored next to the exe."
