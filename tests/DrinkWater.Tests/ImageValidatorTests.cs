using DrinkWater.Api.Storage;

namespace DrinkWater.Tests;

public class ImageValidatorTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] Webp = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8];
    private static readonly byte[] Exe = [0x4D, 0x5A, 0x90, 0x00, 0, 0, 0, 0, 0, 0, 0, 0];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Detects_the_real_format_from_the_first_bytes(byte[] head, string? expected)
    {
        Assert.Equal(expected, ImageValidator.DetectExtension(head));
    }

    public static TheoryData<byte[], string?> Cases => new()
    {
        { Jpeg, ImageValidator.Jpeg },
        { Png, ImageValidator.Png },
        { Webp, ImageValidator.Webp },
        { Exe, null },
        { [0xFF, 0xD8], null }
    };
}
