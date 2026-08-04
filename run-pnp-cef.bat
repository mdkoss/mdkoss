@echo off
setlocal

dotnet run --project src\MDKOSS.Sample\MDKOSS.Sample.csproj -c Debug -r win-x64 -- --setting configs\pnp.setting.json %*
