namespace DrinkWater.Api.Entities;

/// <summary>Cảm xúc thả vào một tin nhắn (mỗi người một cảm xúc mỗi tin, bấm lại để bỏ).</summary>
public class MessageReaction
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public Message Message { get; set; } = null!;
    public Guid UserId { get; set; }
    public string Emoji { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
