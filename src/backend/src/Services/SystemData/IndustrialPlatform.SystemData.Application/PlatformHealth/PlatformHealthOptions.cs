namespace IndustrialPlatform.SystemData.Application.PlatformHealth;

public sealed class PlatformHealthOptions
{
    public const string SectionName = "SystemData:Health";
    public bool Enabled { get; set; } = true;
    public int SampleIntervalSeconds { get; set; } = 30;
    public int CheckTimeoutSeconds { get; set; } = 5;
    public int StaleAfterSeconds { get; set; } = 90;
    public int SampleConcurrency { get; set; } = 4;
}
