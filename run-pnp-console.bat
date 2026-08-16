@echo off
setlocal

dotnet run --project src\MDKOSS.Sample.DieBonder\MDKOSS.Sample.DieBonder.csproj -- --console --setting configs\pnp.setting.json %*
