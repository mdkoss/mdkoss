@echo off
setlocal
cd /d "%~dp0"
dotnet run --project src\MDKOSS.Config.Wpf\MDKOSS.Config.Wpf.csproj -- %*
