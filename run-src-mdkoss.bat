@echo off
setlocal

set "PROJECT=src\MDKOSS.Config\MDKOSS.Config.csproj"

echo [INFO] Running %PROJECT% (WinForms config UI)
dotnet run --project "%PROJECT%"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo [ERROR] Run failed with exit code %EXIT_CODE%.
    pause
    exit /b %EXIT_CODE%
)

echo [INFO] Run exited normally.
pause
exit /b 0
