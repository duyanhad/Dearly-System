namespace DrinkWater.Api.Storage;

public static class ImageValidator
{
    public const string Jpeg = ".jpg";
    public const string Png = ".png";
    public const string Webp = ".webp";

    public static readonly string[] AllowedExtensions = [Jpeg, Png, Webp];

    /// <summary>Identifies the image by its first bytes. The client's file name and
    /// Content-Type are attacker-controlled and are never trusted.</summary>
    public static string? DetectExtension(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            return Jpeg;

        if (head.Length >= 8 &&
            head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 &&
            head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A)
            return Png;

        if (head.Length >= 12 &&
            head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' &&
            head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P')
            return Webp;

        return null;
    }
}
