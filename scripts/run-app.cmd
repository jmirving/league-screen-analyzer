@echo off
setlocal

cd /d "%~dp0\.."

echo.
echo === Stopping existing app process ===
taskkill /IM LeagueScreenAnalyzer.App.exe /F >nul 2>&1

echo.
echo === Cleaning solution ===
dotnet clean LeagueScreenAnalyzer.sln
if errorlevel 1 exit /b 1

echo.
echo === Building solution ===
dotnet build LeagueScreenAnalyzer.sln
if errorlevel 1 exit /b 1

echo.
echo === Launching League Screen Analyzer ===
dotnet run --project src\LeagueScreenAnalyzer.App\LeagueScreenAnalyzer.App.csproj --no-build
exit /b %errorlevel%