@echo off
setlocal

dotnet run --project src\MDKOSS.Cef\MDKOSS.Cef.csproj -- --console --setting configs\pnp.setting.json %*
