namespace IndustrialPlatform.PlatformStudio.Domain;

public sealed class StudioReportArtifact : StudioTechnicalRecord
{
    public Guid RunId { get; set; }
    public string Purpose { get; set; } = "Output";
    public string Format { get; set; } = "Pdf";
    public string FileNId { get; set; } = "";
    public string FileReferenceNId { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long ContentLength { get; set; }
    public int? PageCount { get; set; }
    public string EngineManifestJson { get; set; } = "{}";
    public DateTimeOffset? RetentionUntil { get; set; }
}
