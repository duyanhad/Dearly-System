namespace DrinkWater.Api.Dtos;

/// <summary>Một tệp/nội dung đính kèm nhiệm vụ trả về cho app.</summary>
public record DutyMediaDto(
    int Id,
    string Side,
    string Kind,
    string? Text,
    string? Url,
    int DurationMs,
    int Attempt);

/// <summary>Nhiệm vụ đầy đủ để hiện trong danh sách + chi tiết.</summary>
public record DutyDto(
    int Id,
    Guid AssignerId,
    Guid AssigneeId,
    string AssignerName,
    string? AssignerAvatar,
    string AssigneeName,
    string? AssigneeAvatar,
    string Title,
    string Note,
    DateTime StartsAtUtc,
    int RemindBeforeMin,
    DateTime DueAtUtc,
    /// <summary>Người nhận đã bấm "đã nhận nhiệm vụ" lúc nào.</summary>
    DateTime? AcceptedAtUtc,
    string Status,
    int Attempt,
    DateTime CreatedAt,
    DateTime? SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    string? ReviewNote,
    /// <summary>true = tôi là người giao việc.</summary>
    bool Mine,
    /// <summary>Đã quá hạn báo cáo mà chưa được duyệt.</summary>
    bool Overdue,
    /// <summary>Người nhận chưa bấm "đã nhận nhiệm vụ".</summary>
    bool NeedsAccept,
    List<DutyMediaDto> Media);

/// <summary>Sửa nhiệm vụ (chỉ người giao, không đổi tệp đính kèm).</summary>
public record UpdateDutyRequest(string Title, string? Note, DateTime StartsAtUtc, int? RemindBeforeMin, DateTime DueAtUtc);

/// <summary>Duyệt báo cáo: đạt hay chưa đạt (kèm lý do).</summary>
public record ReviewDutyRequest(bool Approve, string? Note);

/// <summary>Người nhận từ chối nhiệm vụ, kèm lý do (không bắt buộc).</summary>
public record DeclineDutyRequest(string? Note);
/// <summary>Một nhiệm vụ trong lô giao nhiều việc (gửi lên dạng JSON, tệp đính kèm theo chỉ số).</summary>
public class BatchDutyItem
{
    public string? Title { get; set; }
    public string? Note { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime DueAtUtc { get; set; }
    public int? RemindBeforeMin { get; set; }
    public string? Text { get; set; }
    public int? VoiceDurationMs { get; set; }
}
/// <summary>Một việc bị từ chối trong lô, kèm ghi chú riêng (không bắt buộc).</summary>
public record DutyDeclineItem(int Id, string? Note);
/// <summary>Người nhận trả lời cả lô: nhận những việc này, từ chối những việc kia (lý do dùng chung).</summary>
public record RespondDutiesRequest(List<int>? Accepts, List<DutyDeclineItem>? Declines, string? Reason);
