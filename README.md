# IndustrialPlatform

This milestone establishes skeleton infrastructure only; it does not implement business features.

## Service order

BuildingBlocks → Identity → ReferenceData → MasterData

## Commands

From the repository root, set the local .NET CLI home and run:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet_cli_home')
dotnet restore IndustrialPlatform.slnx
dotnet build IndustrialPlatform.slnx --no-restore
dotnet test IndustrialPlatform.slnx --no-build
```
