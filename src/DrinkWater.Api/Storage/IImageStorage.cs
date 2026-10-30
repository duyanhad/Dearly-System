namespace DrinkWater.Api.Storage;

public interface IImageStorage
{
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default);
}

public sealed class StorageOptions
{
    public string Provider { get; set; } = "Local";
    public long MaxBytes { get; set; } = 5 * 1024 * 1024;
}
