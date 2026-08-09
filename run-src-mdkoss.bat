@echo off
setlocal

set "PROJECT=src\MDKOSS.Config.Wpf\MDKOSS.Config.Wpf.csproj"

echo [INFO] Running %PROJECT% (WPF config UI)
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
