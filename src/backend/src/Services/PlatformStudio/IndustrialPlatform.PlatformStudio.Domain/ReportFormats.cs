namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>Production export contract. Raster formats contain all physical pages in a ZIP.</summary>
public static class ReportFormats
{
    public static bool Supports(string format) => format is "Pdf" or "Html" or "Xls" or "Xlsx" or "Docx" or "Rtf" or "Mht" or "Text" or "Csv" or "Png" or "Jpeg";
    public static string ContentType(string format) => format switch
    {
        "Pdf" => "application/pdf", "Html" => "text/html", "Xls" => "application/vnd.ms-excel",
        "Xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "Docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "Rtf" => "application/rtf", "Mht" => "multipart/related", "Text" => "text/plain", "Csv" => "text/csv",
        "Png" or "Jpeg" => "application/zip", "Json" => "application/json",
        _ => throw new StudioException("REPORT_FORMAT_UNSUPPORTED", "输出格式未登记。", 422)
    };
    public static string Extension(string contentType) => contentType switch
    {
        "application/pdf" => ".pdf", "text/html" => ".html", "application/vnd.ms-excel" => ".xls",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "application/rtf" => ".rtf", "multipart/related" => ".mht", "text/plain" => ".txt", "text/csv" => ".csv",
        "application/zip" => ".zip", _ => ".json"
    };
}
