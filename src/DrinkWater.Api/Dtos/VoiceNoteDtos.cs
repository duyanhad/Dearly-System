using System.ComponentModel.DataAnnotations;

namespace DrinkWater.Api.Dtos;

/// <summary>Lời nhắn thoại hẹn giờ (gửi cho partner/bạn bè).</summary>
public record VoiceNoteDto(
    int Id,
    Guid SenderId,
    string SenderName,
    string? SenderAvatar,
    string AudioUrl,
    int DurationMs,
    DateTime ScheduledAtUtc,
    DateTime? ListenedAtUtc,
    int RingCount,
    int Round = 0);

public record CreateVoiceNoteForm(Guid ReceiverId, DateTime ScheduledAtUtc, int DurationMs);