@echo off
setlocal

cd /d "%~dp0\.."

echo.
echo === Restoring packages ===
dotnet restore LeagueMapCapture.sln
if errorlevel 1 exit /b 1

echo.
echo === Building solution ===
dotnet build LeagueMapCapture.sln --no-restore
if errorlevel 1 exit /b 1

echo.
echo === Running tests ===
dotnet test LeagueMapCapture.sln --no-build
if errorlevel 1 exit /b 1

echo.
echo === Verification successful ===
exit /b 0