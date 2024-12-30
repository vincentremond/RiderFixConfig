@ECHO OFF

dotnet tool restore
dotnet build -- %*

AddToPath .\RiderFixConfig\bin\Debug
