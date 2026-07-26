@echo off
setlocal

pushd "%~dp0.."
if errorlevel 1 exit /b 1

echo Restoring solution...
dotnet restore LeagueScreenAnalyzer.sln
if errorlevel 1 goto :failure

echo Building solution with warnings treated as errors...
dotnet build LeagueScreenAnalyzer.sln --no-restore -warnaserror
if errorlevel 1 goto :failure

echo Running tests...
dotnet test LeagueScreenAnalyzer.sln --no-build
if errorlevel 1 goto :failure

set "VERIFY_OUTPUT=artifacts\verify-valid-continuous"
if exist "%VERIFY_OUTPUT%" rmdir /s /q "%VERIFY_OUTPUT%"

echo Processing valid fixture...
dotnet run --project src\LeagueScreenAnalyzer.Cli --no-build -- process-fixture --source fixtures\valid-continuous\session.json --output "%VERIFY_OUTPUT%"
if errorlevel 1 goto :failure

if not exist "%VERIFY_OUTPUT%\timeline.jsonl" (
    echo Missing expected timeline: %VERIFY_OUTPUT%\timeline.jsonl
    goto :failure
)

if not exist "%VERIFY_OUTPUT%\summary.json" (
    echo Missing expected summary: %VERIFY_OUTPUT%\summary.json
    goto :failure
)

set "CLOCK_OUTPUT=artifacts\clock-evaluation"
if exist "%CLOCK_OUTPUT%" rmdir /s /q "%CLOCK_OUTPUT%"

echo Evaluating committed clock fixtures...
dotnet run --project src\LeagueScreenAnalyzer.Cli --no-build -- evaluate-clock --profile league-replay-v1 --manifest fixtures\clocks\synthetic-seven-segment\manifest.json --output "%CLOCK_OUTPUT%"
if errorlevel 1 goto :failure

if not exist "%CLOCK_OUTPUT%\clock-evaluation.json" (
    echo Missing expected clock evaluation: %CLOCK_OUTPUT%\clock-evaluation.json
    goto :failure
)

echo Verification succeeded.
popd
exit /b 0

:failure
echo Verification failed.
popd
exit /b 1
