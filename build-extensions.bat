@echo off
setlocal

set "PROJECT=src\MDKOSS.Extensions\MDKOSS.Extensions.csproj"

echo [INFO] Building %PROJECT%
dotnet build "%PROJECT%"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo [ERROR] Build failed with exit code %EXIT_CODE%.
    pause
    exit /b %EXIT_CODE%
)

echo [INFO] Build succeeded.
echo [INFO] Output: src\MDKOSS.Extensions\bin\Debug\net8.0-windows10.0.22621.0\MDKOSS.Extensions.dll
pause
exit /b 0
