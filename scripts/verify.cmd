@echo off
setlocal

cd /d "%~dp0\.."

echo.
echo === Restoring packages ===
dotnet restore LeagueScreenAnalyzer.sln
if errorlevel 1 exit /b 1

echo.
echo === Building solution ===
dotnet build LeagueScreenAnalyzer.sln --no-restore
if errorlevel 1 exit /b 1

echo.
echo === Running tests ===
dotnet test LeagueScreenAnalyzer.sln --no-build
if errorlevel 1 exit /b 1

echo.
echo === Verification successful ===
exit /b 0