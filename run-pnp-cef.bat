@echo off
setlocal

dotnet run --project src\MDKOSS.Sample.Pnp\MDKOSS.Sample.Pnp.csproj -c Debug -r win-x64 %*
