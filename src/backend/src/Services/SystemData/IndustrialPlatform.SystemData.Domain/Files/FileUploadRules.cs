using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.SystemData.Domain.Files;

/// <summary>
/// File upload invariants shared by the session API and storage adapter.
/// </summary>
public static class FileUploadRules
{
    public const int SampleWindowBytes = 65_536;

    public static string NormalizeSha256(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length != 64 || normalized.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new ArgumentException("必须提供 64 位十六进制 SHA-256。", nameof(value));
        }

        return normalized;
    }

    public static string? NormalizeOptionalSha256(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeSha256(value);

    public static string SanitizeFileName(string? value)
    {
        var name = Path.GetFileName(value?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            throw new ArgumentException("文件名不能为空。", nameof(value));
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsControl(character) || invalid.Contains(character) ? '_' : character);
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? "file" : result[..Math.Min(result.Length, 240)];
    }

    public static string ComputeSampleFingerprint(ReadOnlySpan<byte> content, int windowBytes = SampleWindowBytes)
    {
        if (windowBytes <= 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowBytes);
        }

        using var buffer = new MemoryStream();
        buffer.Write("IPF:sample-v1\n"u8);
        WriteInt64(buffer, content.Length);

        if (content.Length <= windowBytes * 3L)
        {
            WriteWindow(buffer, content, 0, content.Length);
        }
        else
        {
            WriteWindow(buffer, content, 0, windowBytes);
            WriteWindow(buffer, content, (content.Length - windowBytes) / 2, windowBytes);
            WriteWindow(buffer, content, content.Length - windowBytes, windowBytes);
        }

        return $"sample-v1:{Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant()}";
    }

    private static void WriteWindow(Stream target, ReadOnlySpan<byte> content, int offset, int length)
    {
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteInt64BigEndian(header, offset);
        BinaryPrimitives.WriteInt32BigEndian(header[8..], length);
        target.Write(header);
        target.Write(content[offset..(offset + length)]);
    }

    private static void WriteInt64(Stream target, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        target.Write(buffer);
    }
}
