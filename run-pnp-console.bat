@echo off
setlocal

dotnet run --project src\MDKOSS.Sample\MDKOSS.Sample.csproj -- --console --setting configs\pnp.setting.json %*
