using System.ComponentModel.DataAnnotations;

namespace DrinkWater.Api.Dtos;

public record FeedItemDto(
    Guid UserId,
    string FullName,
    string? Nickname,
    string? AvatarUrl,
    string Email,
    int TotalDrankMl,
    int TargetMl,
    int Percent,
    bool IsCompleted,
    string? LatestImageUrl,
    string? LatestImageNote,
    DateTime? LatestImageAt);

public record NudgeResultDto(bool DeliveredRealtime, bool DeliveredPush);

/// <summary>Loại nhắc (water/meal/medicine/study/sleep/weight) + lời nhắn tự soạn.</summary>
public record NudgeRequest(string? Kind, string? Message);

public record NotificationDto(
    int Id, Guid? SenderId, string Title, string Message, string? ImageUrl,
    string Type, bool IsRead, DateTime CreatedAt);

public record BroadcastRequest(
    [Required, MaxLength(100)] string Title,
    [Required, MaxLength(255)] string Message);

public record AdminUserDto(
    Guid Id, string Email, string FullName, string Role, bool IsLocked, DateTime CreatedAt,
    int TotalLogs, string? AvatarUrl);

public record UpdateUserStatusRequest(bool IsLocked);

public record UpdateUserRoleRequest([Required] string Role);

public record SystemLogDto(
    int Id, Guid? UserId, string Action, string? Details, string LogLevel, DateTime CreatedAt);

public record PagedResult<T>(List<T> Items, int Page, int PageSize, int Total);

public record PendingProofDto(
    string Id, Guid UserId, string FullName, string? AvatarUrl, int AmountMl, string Period,
    string? ImageUrl, string? Note, DateTime LogTime,
    // "water" | "sleep" | "medicine" | "meal" | "study" | "weight"
    string Kind = "water",
    // Cân nặng: gam · Giấc ngủ: số phút · các nhu cầu khác: null
    int? Value = null,
    string? BedTime = null,
    string? WakeTime = null);

public record ReviewProofRequest(bool Approve, [MaxLength(200)] string? Message = null);

public record SetPartnerRequest(Guid? PartnerId);

public record PartnerClientDto(
    Guid Id, string FullName, string Email, string? AvatarUrl,
    int TotalDrankMl, int TargetMl, string? CupImageUrl, string? Nickname, string? CupCutoutUrl = null);

/// <summary>Tiến độ một nhu cầu trong ngày của người mình duyệt (thanh tiến độ trang partner).</summary>
/// <remarks>Target/Done theo đơn vị của nhu cầu: ml · số lần · phút ngủ · gam.</remarks>
public record PartnerNeedDto(
    string Kind, int Target, int Done, int Percent, int Pending, int PendingCount,
    string? ImageUrl = null, string? CutoutUrl = null,
    /// <summary>Giấc ngủ: người này đang trong giấc (bộ đếm đang chạy).</summary>
    bool Sleeping = false);

/// <summary>Người duyệt ảnh của tôi (partner) + tiến độ hôm nay của họ.</summary>
public record MyPartnerDto(
    Guid Id, string FullName, string? AvatarUrl,
    int TotalDrankMl, int TargetMl, string? CupImageUrl);

/// <summary>Partner nhập số ml muốn trừ khỏi cốc của người kia.</summary>
public record DeductRequest([Range(1, 5000)] int AmountMl);

public record DeductResultDto(
    int AmountMl, int TotalDrankMl, int TargetMl, int Percent, string PartnerName);

/// <summary>Một người trong danh sách bạn bè: biệt hiệu tôi đặt, biệt hiệu họ đặt cho tôi,
/// và quan hệ theo hai chiều.</summary>
public record FriendDto(
    Guid Id,
    string Email,
    string FullName,
    string? Nickname,
    string? TheirNickname,
    string? AvatarUrl,
    bool IFollow,
    bool TheyFollow,
    bool IsMyPartner,
    // Tôi đã xin người này làm người duyệt ảnh, đang chờ họ đồng ý.
    bool IsPendingPartner = false);

/// <summary>Người tôi xin duyệt ảnh (chờ đồng ý) hoặc người xin tôi duyệt ảnh.</summary>
public record PartnerRequestDto(Guid Id, string FullName, string? AvatarUrl, string? Nickname = null);

/// <summary>Phiếu đã duyệt / đã từ chối, kèm giờ duyệt — để xem lại ảnh ở trang Partner.</summary>
public record ReviewedProofDto(
    string Id,
    string Kind,
    int AmountMl,
    string Period,
    string? ImageUrl,
    string? Note,
    DateTime LogTime,
    DateTime? ReviewedAt,
    bool Approved,
    string? RejectReason = null,
    int? Value = null);

public record SetNicknameRequest([MaxLength(50)] string? Nickname);
