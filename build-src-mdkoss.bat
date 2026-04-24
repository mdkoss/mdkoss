@echo off
setlocal

set "PROJECT=src\MDKOSS.csproj"

echo [INFO] Building %PROJECT%
dotnet build "%PROJECT%"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo [ERROR] Build failed with exit code %EXIT_CODE%.
    pause
    exit /b %EXIT_CODE%
)

echo [INFO] Build succeeded.
pause
exit /b 0
