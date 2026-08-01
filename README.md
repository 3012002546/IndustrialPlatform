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

## Documentation

- [Architecture blueprint](docs/blueprint/README.md)
- [Implementation guide](docs/implementation/README.md)
- [Bootstrap design](docs/superpowers/specs/2026-08-01-industrial-platform-bootstrap-design.md)
- [Bootstrap plan](docs/superpowers/plans/2026-08-01-industrial-platform-bootstrap.md)

## Milestone scope

Login/JWT/permissions, ReferenceData features, persistence, Redis, RabbitMQ, front end, containers, and deployment remain future work.
