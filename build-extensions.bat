@echo off
setlocal

set "PROJECT=src\extensions\MDKOSS.Extensions.csproj"

echo [INFO] Building extensions only: %PROJECT%
dotnet build "%PROJECT%"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo [ERROR] Build failed with exit code %EXIT_CODE%.
    pause
    exit /b %EXIT_CODE%
)

echo [INFO] Build succeeded.
echo [INFO] Output: src\extensions\bin\Debug\net8.0-windows10.0.22621.0\MDKOSS.Extensions.dll
pause
exit /b 0
