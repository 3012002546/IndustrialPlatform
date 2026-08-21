using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

[SugarTable("system_data_seed_ledger")]
public sealed class SystemDataSeedLedgerTable : ControlPlaneEntityTable
{
    [SugarColumn(ColumnName = "seed_key")] public string SeedKey { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "seed_version")] public string SeedVersion { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "checksum")] public string Checksum { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "applied_on")] public DateTimeOffset AppliedOn { get; set; }
}
