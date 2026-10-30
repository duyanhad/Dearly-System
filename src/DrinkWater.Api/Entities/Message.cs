namespace DrinkWater.Api.Entities;

/// <summary>Tin nhắn chat 1-1 giữa hai người là bạn bè.</summary>
public class Message
{
    public int Id { get; set; }
    public Guid SenderId { get; set; }
    public Guid ReceiverId { get; set; }
    public string Body { get; set; } = string.Empty;
    /// <summary>Tin nhắn thoại: đường dẫn file ghi âm (null với tin chữ).</summary>
    public string? AudioUrl { get; set; }
    public int? AudioDurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
