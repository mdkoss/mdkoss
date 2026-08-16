@echo off
setlocal

dotnet run --project src\MDKOSS.Sample.Pnp\MDKOSS.Sample.Pnp.csproj -- --console %*
