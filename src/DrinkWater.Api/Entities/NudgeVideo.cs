namespace DrinkWater.Api.Entities;

/// <summary>
/// Video ngắn (khoảng 7–8 giây) admin tải lên cho từng nhu cầu, gửi kèm khi bấm "Nhắc nhở".
/// Mỗi nhu cầu có thể có nhiều video; mỗi lần nhắc lấy ngẫu nhiên một video đang bật.
/// </summary>
public class NudgeVideo
{
    public int Id { get; set; }
    /// <summary>water | meal | medicine | study | sleep | weight</summary>
    public string Kind { get; set; } = "water";
    /// <summary>Đường dẫn tương đối do storage trả về (giống ảnh minh chứng).</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>Nhãn hiển thị cho admin (không bắt buộc).</summary>
    public string? Label { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? UploadedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
