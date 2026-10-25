@echo off
setlocal

dotnet run --project src\MDKOSS.Sample.Dispenser\MDKOSS.Sample.Dispenser.csproj -c Debug -r win-x64 %*
