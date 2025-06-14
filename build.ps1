$ErrorActionPreference = "Stop"

dotnet tool restore
dotnet build

AddToPath .\RiderFixConfig\bin\Debug
