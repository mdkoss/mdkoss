@echo off
setlocal

dotnet run --project src\MDKOSS.Cef\MDKOSS.Cef.csproj -c Debug -r win-x64 -- --setting configs\pnp.setting.json %*
