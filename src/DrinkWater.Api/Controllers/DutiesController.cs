using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Hubs;
using DrinkWater.Api.Services;
using DrinkWater.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Controllers;

/// <summary>
/// Nhiệm vụ giữa 2 partner: người giao đặt tên + hạn, người nhận báo cáo bằng ảnh/voice/tin nhắn
/// (chọn nhiều), người giao duyệt đạt hoặc chưa đạt. Mọi bước đều đẩy thông báo cho cả hai bên.
/// </summary>
[ApiController]
[Authorize]
[Route("api/duties")]
public class DutiesController(
    AppDbContext db,
    IImageStorage storage,
    IHubContext<NotificationHub> hub,
    IWebPushSender webPush,
    IClock clock) : ControllerBase
{
    private const int MaxFiles = 6;
    private const int MaxVoiceBytes = 5 * 1024 * 1024;

    private static string? VoiceExtension(string? contentType) => contentType?.Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "audio/mp4" or "audio/m4a" or "audio/x-m4a" => ".m4a",
        "audio/webm" => ".webm",
        "audio/ogg" => ".ogg",
        "audio/mpeg" => ".mp3",
        "audio/wav" or "audio/x-wav" => ".wav",
        _ => null,
    };

    /// <summary>Danh sách nhiệm vụ: box=mine (được giao) hoặc given (tôi giao).</summary>
    [HttpGet]
    public async Task<List<DutyDto>> List([FromQuery] string box = "mine", CancellationToken ct = default)
    {
        var me = User.GetUserId();
        var query = db.Duties.AsNoTracking().AsQueryable();
        query = box == "given" ? query.Where(d => d.AssignerId == me) : query.Where(d => d.AssigneeId == me);

        var rows = await query
            .OrderBy(d => d.Status == DutyStatuses.Approved ? 1 : 0)
            .ThenBy(d => d.DueAtUtc)
            .Take(60)
            .Include(d => d.Assigner)
            .Include(d => d.Assignee)
            .Include(d => d.Media)
            .ToListAsync(ct);

        var now = clock.UtcNow;
        return rows.Select(d => ToDto(d, me, now)).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DutyDto>> Get(int id, CancellationToken ct)
    {
        var me = User.GetUserId();
        var duty = await LoadAsync(id, ct);
        if (duty is null) return NotFound();
        if (duty.AssignerId != me && duty.AssigneeId != me) return Forbid();

        return ToDto(duty, me, clock.UtcNow);
    }

    /// <summary>
    /// Giao nhiệm vụ cho partner. multipart: title, note?, dueAtUtc, text? (tin nhắn kèm theo),
    /// files[] (ảnh hoặc ghi âm, chọn nhiều).
    /// </summary>
    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = 40 * 1024 * 1024)]
    public async Task<ActionResult<DutyDto>> Create(
        [FromForm] string title,
        [FromForm] DateTime startsAtUtc,
        [FromForm] DateTime dueAtUtc,
        [FromForm] int? remindBeforeMin,
        [FromForm] string? note,
        [FromForm] string? text,
        [FromForm] int? voiceDurationMs,
        List<IFormFile>? files,
        CancellationToken ct)
    {
        var me = User.GetUserId();
        var partner = await PartnerOfAsync(me, ct);
        if (partner is null)
            return Problem(statusCode: 400, title: "Chưa có partner nên chưa giao được nhiệm vụ");

        var clean = (title ?? string.Empty).Trim();
        if (clean.Length is 0 or > 120) return Problem(statusCode: 400, title: "Tên nhiệm vụ 1–120 ký tự");

        var start = ToUtc(startsAtUtc);
        var due = ToUtc(dueAtUtc);
        if (start < clock.UtcNow.AddMinutes(-1))
            return Problem(statusCode: 400, title: "Giờ bắt đầu phải ở tương lai");
        if (due <= start)
            return Problem(statusCode: 400, title: "Hạn báo cáo phải sau giờ bắt đầu");

        var duty = new Duty
        {
            AssignerId = me,
            AssigneeId = partner.Value.Id,
            Title = clean,
            Note = (note ?? string.Empty).Trim(),
            StartsAtUtc = start,
            RemindBeforeMin = Math.Clamp(remindBeforeMin ?? 10, 0, 240),
            DueAtUtc = due,
        };

        var mediaError = await AttachAsync(duty, DutySides.Assign, text, voiceDurationMs, files, 0, ct);
        if (mediaError is not null) return Problem(statusCode: 400, title: mediaError);

        db.Duties.Add(duty);
        await db.SaveChangesAsync(ct);

        var meName = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync(ct);
        await NotifyAsync(partner.Value.Id, "📋 Nhiệm vụ mới",
            $"{meName} giao cho bạn: “{clean}” — bắt đầu {LocalLabel(duty.StartsAtUtc)}, hạn {LocalLabel(due)}. "
            + "Mở Dearly bấm “Đã nhận nhiệm vụ” để thôi nhắc.", ct);

        var saved = await LoadAsync(duty.Id, ct);
        return ToDto(saved!, me, clock.UtcNow);
    }

    /// <summary>Sửa tên / mô tả / giờ bắt đầu / nhắc trước / hạn (chỉ người giao).</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<DutyDto>> Update(int id, UpdateDutyRequest req, CancellationToken ct)
    {
        var me = User.GetUserId();
        var duty = await LoadAsync(id, ct);
        if (duty is null) return NotFound();
        if (duty.AssignerId != me) return Forbid();

        var clean = (req.Title ?? string.Empty).Trim();
        if (clean.Length is 0 or > 120) return Problem(statusCode: 400, title: "Tên nhiệm vụ 1–120 ký tự");

        var start = ToUtc(req.StartsAtUtc);
        var due = ToUtc(req.DueAtUtc);
        if (due <= start)
            return Problem(statusCode: 400, title: "Hạn báo cáo phải sau giờ bắt đầu");

        duty.Title = clean;
        duty.Note = (req.Note ?? string.Empty).Trim();
        duty.StartsAtUtc = start;
        duty.RemindBeforeMin = Math.Clamp(req.RemindBeforeMin ?? duty.RemindBeforeMin, 0, 240);
        duty.DueAtUtc = due;
        duty.StartNotifiedAtUtc = null;
        duty.DueSoonNotifiedAtUtc = null;
        duty.OverdueNotifiedAtUtc = null;
        await db.SaveChangesAsync(ct);

        await NotifyAsync(duty.AssigneeId, "📋 Nhiệm vụ đã đổi",
            $"“{clean}” vừa được cập nhật: bắt đầu {LocalLabel(start)}, hạn {LocalLabel(due)}.", ct);

        return ToDto(duty, me, clock.UtcNow);
    }

    /// <summary>Người nhận bấm "đã nhận nhiệm vụ" — thôi kêu lặp.</summary>
    [HttpPost("{id:int}/accept")]
    public async Task<ActionResult<DutyDto>> Accept(int id, CancellationToken ct)
    {
        var me = User.GetUserId();
        var duty = await LoadAsync(id, ct);
        if (duty is null) return NotFound();
        if (duty.AssigneeId != me) return Forbid();
        if (duty.Status is DutyStatuses.Approved or DutyStatuses.Declined)
            return Problem(statusCode: 400, title: "Nhiệm vụ này đã đóng");

        if (duty.AcceptedAtUtc is null)
        {
            duty.AcceptedAtUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);

            var meName = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync(ct);
            await NotifyAsync(duty.AssignerId, "✅ Đã nhận nhiệm vụ",
                $"{meName} đã nhận “{duty.Title}”. Bạn sẽ được báo khi có báo cáo.", ct);
        }

        return ToDto(duty, me, clock.UtcNow);
    }

    /// <summary>Người nhận từ chối nhiệm vụ (kèm lý do nếu muốn) — nhiệm vụ đóng lại.</summary>
    [HttpPost("{id:int}/decline")]
    public async Task<ActionResult<DutyDto>> Decline(int id, DeclineDutyRequest req, CancellationToken ct)
    {
        var me = User.GetUserId();
        var duty = await LoadAsync(id, ct);
        if (duty is null) return NotFound();
        if (duty.AssigneeId != me) return Forbid();
        if (duty.Status is DutyStatuses.Approved or DutyStatuses.Declined)
            return Problem(statusCode: 400, title: "Nhiệm vụ này đã đóng");

        duty.Status = DutyStatuses.Declined;
        duty.AcceptedAtUtc ??= clock.UtcNow;
        duty.ReviewedAtUtc = clock.UtcNow;
        duty.ReviewNote = (req.Note ?? string.Empty).Trim();
        await db.SaveChangesAsync(ct);

        var meName = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync(ct);
        var body = $"{meName} đã từ chối “{duty.Title}”"
                   + (string.IsNullOrEmpty(duty.ReviewNote) ? "." : $": {duty.ReviewNote}");
        await NotifyAsync(duty.AssignerId, "🚫 Nhiệm vụ bị từ chối", body, ct);

        return ToDto(duty, me, clock.UtcNow);
    }

    /// <summary>
    /// Người nhận thu hồi báo cáo vừa gửi để sửa lại: bỏ bằng chứng của lần gần nhất, nhiệm vụ trở về
    /// trạng thái đang làm để người giao không còn thấy báo cáo đó nữa.
    /// </summary>
    [HttpPost("{id:int}/withdraw")]
    public async Task<ActionResult<DutyDto>> Withdraw(int id, CancellationToken ct)
    {
        var me = User.GetUserId();
        var duty = await LoadAsync(id, ct);
        if (duty is null) return NotFound();
        if (duty.AssigneeId != me) return Forbid();
        if (duty.Status != DutyStatuses.Submitted)
            return Problem(statusCode: 400, title: "Chưa có báo cáo nào để sửa");

        var last = duty.Attempt;
        var removed = duty.Media.Where(m => m.Side == DutySides.Report && m.Attempt == last).ToList();
        db.DutyMedia.RemoveRange(removed);

        duty.Status = DutyStatuses.Open;
        duty.SubmittedAtUtc = null;
        duty.ReviewedAtUtc = null;
        duty.ReviewNote = null;
        await db.SaveChangesAsync(ct);

        var meName = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync(ct);
        await NotifyAsync(duty.AssignerId, "✏️ Đang sửa báo cáo",
            $"{meName} thu hồi báo cáo “{duty.Title}” để sửa. Bạn sẽ nhận lại khi họ gửi xong.", ct);

        return ToDto(duty, me, clock.UtcNow);
    }

    /// <summary>Xoá nhiệm vụ (chỉ người giao).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var me = User.GetUserId();
        var duty = await db.Duties.Include(d => d.Media).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (duty is null) return NotFound();
        if (duty.AssignerId != me) return Forbid();

        db.Duties.Remove(duty);
        await db.SaveChangesAsync(ct);
        await NotifyAsync(duty.AssigneeId, "📋 Nhiệm vụ đã huỷ", $"“{duty.Title}” đã được người giao xoá.", ct);
        return NoContent();
    }

    /// <summary>
    /// Báo cáo hoàn thành (chỉ người nhận). multipart: note?, text?, files[] (ảnh/ghi âm, chọn nhiều).
    /// Báo cáo sau hạn vẫn được, hệ thống ghi rõ "báo cáo muộn" cho người giao.
    /// </summary>
    [HttpPost("{id:int}/report")]
    [RequestFormLimits(MultipartBodyLengthLimit = 40 * 1024 * 1024)]
    public async Task<ActionResult<DutyDto>> Report(
        int id,
        [FromForm] string? note,
        [FromForm] string? text,
        [FromForm] int? voiceDurationMs,
        List<IFormFile>? files,
        CancellationToken ct)
    {
        var me = User.GetUserId();
        var duty = await LoadAsync(id, ct);
        if (duty is null) return NotFound();
        if (duty.AssigneeId != me) return Forbid();
        if (duty.AcceptedAtUtc is null)
            return Problem(statusCode: 400, title: "Bấm “Đã nhận nhiệm vụ” trước khi báo cáo");
        if (duty.Status == DutyStatuses.Approved)
            return Problem(statusCode: 400, title: "Nhiệm vụ này đã được duyệt đạt");

        var attempt = duty.Attempt + 1;
        var mediaError = await AttachAsync(duty, DutySides.Report, text, voiceDurationMs, files, attempt, ct);
        if (mediaError is not null) return Problem(statusCode: 400, title: mediaError);

        var hasContent = duty.Media.Any(m => m.Side == DutySides.Report && m.Attempt == attempt);
        if (!hasContent) return Problem(statusCode: 400, title: "Thêm ít nhất một ảnh, ghi âm hoặc tin nhắn");

        var now = clock.UtcNow;
        duty.Attempt = attempt;
        duty.Status = DutyStatuses.Submitted;
        duty.SubmittedAtUtc = now;
        duty.ReviewNote = null;
        await db.SaveChangesAsync(ct);

        var meName = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync(ct);
        var late = now > duty.DueAtUtc ? " (báo cáo muộn)" : string.Empty;
        var extra = string.IsNullOrWhiteSpace(note) ? string.Empty : $" Ghi chú: {note.Trim()}";
        await NotifyAsync(duty.AssignerId, "📋 Có báo cáo mới",
            $"{meName} đã báo cáo “{duty.Title}”{late}.{extra} Vào Nhiệm vụ để duyệt.", ct);

        return ToDto(duty, me, now);
    }

    /// <summary>Duyệt báo cáo: đạt (đóng nhiệm vụ) hoặc chưa đạt (người nhận làm lại).</summary>
    [HttpPost("{id:int}/review")]
    public async Task<ActionResult<DutyDto>> Review(int id, ReviewDutyRequest req, CancellationToken ct)
    {
        var me = User.GetUserId();
        var duty = await LoadAsync(id, ct);
        if (duty is null) return NotFound();
        if (duty.AssignerId != me) return Forbid();
        if (duty.Status != DutyStatuses.Submitted)
            return Problem(statusCode: 400, title: "Nhiệm vụ này chưa có báo cáo để duyệt");

        duty.Status = req.Approve ? DutyStatuses.Approved : DutyStatuses.Rejected;
        duty.ReviewedAtUtc = clock.UtcNow;
        duty.ReviewNote = (req.Note ?? string.Empty).Trim();
        await db.SaveChangesAsync(ct);

        var title = req.Approve ? "✅ Nhiệm vụ đạt" : "🔁 Nhiệm vụ chưa đạt";
        var body = req.Approve
            ? $"“{duty.Title}” đã được duyệt. Làm tốt lắm!"
            : $"“{duty.Title}” cần làm lại" +
              (string.IsNullOrEmpty(duty.ReviewNote) ? "." : $": {duty.ReviewNote}");
        await NotifyAsync(duty.AssigneeId, title, body, ct);

        return ToDto(duty, me, clock.UtcNow);
    }

    // ---------- helpers ----------

    private Task<Duty?> LoadAsync(int id, CancellationToken ct) =>
        db.Duties
            .Include(d => d.Assigner)
            .Include(d => d.Assignee)
            .Include(d => d.Media)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    private async Task<(Guid Id, string Name)?> PartnerOfAsync(Guid me, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == me)
            .Select(u => new { u.PartnerId }).FirstOrDefaultAsync(ct);
        if (user?.PartnerId is not { } partnerId) return null;
        var partner = await db.Users.AsNoTracking().Where(u => u.Id == partnerId)
            .Select(u => new { u.Id, u.FullName }).FirstOrDefaultAsync(ct);
        return partner is null ? null : (partner.Id, partner.FullName);
    }

    /// <summary>Lưu file + tin nhắn chữ vào nhiệm vụ. Trả về thông báo lỗi nếu tệp không hợp lệ.</summary>
    private async Task<string?> AttachAsync(
        Duty duty, string side, string? text, int? voiceDurationMs,
        List<IFormFile>? files, int attempt, CancellationToken ct)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length > 0)
        {
            if (trimmed.Length > 1000) return "Tin nhắn tối đa 1000 ký tự";
            duty.Media.Add(new DutyMedia
            {
                Side = side,
                Kind = DutyMediaKinds.Text,
                Text = trimmed,
                Attempt = attempt,
            });
        }

        if (files is null || files.Count == 0) return null;
        if (files.Count > MaxFiles) return $"Tối đa {MaxFiles} tệp mỗi lần";

        foreach (var file in files)
        {
            if (file.Length == 0) continue;

            if (file.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            {
                var (extension, error) = await ImageUpload.PrepareAsync(file, 8 * 1024 * 1024);
                if (error is not null) return error;
                await using var stream = file.OpenReadStream();
                var url = await storage.SaveAsync(stream, extension!, ct);
                duty.Media.Add(new DutyMedia { Side = side, Kind = DutyMediaKinds.Image, Url = url, Attempt = attempt });
                continue;
            }

            var voiceExtension = VoiceExtension(file.ContentType);
            if (voiceExtension is null) continue;
            if (file.Length > MaxVoiceBytes) return "Ghi âm tối đa 5 MB";

            await using var audio = file.OpenReadStream();
            var voiceUrl = await storage.SaveAsync(audio, voiceExtension, ct);
            duty.Media.Add(new DutyMedia
            {
                Side = side,
                Kind = DutyMediaKinds.Voice,
                Url = voiceUrl,
                DurationMs = Math.Clamp(voiceDurationMs ?? 0, 0, 600_000),
                Attempt = attempt,
            });
        }

        return null;
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private string LocalLabel(DateTime utc)
    {
        var local = utc + (clock.LocalNow - clock.UtcNow);
        return local.ToString("HH:mm 'ngày' dd/MM");
    }

    /// <summary>Thông báo cho một người: realtime (app mở) + web push (app đóng) + lưu vào chuông.</summary>
    private async Task NotifyAsync(Guid userId, string title, string body, CancellationToken ct)
    {
        db.Notifications.Add(new Notification
        {
            ReceiverId = userId,
            Title = title,
            Message = body,
            Type = NotificationTypes.Duty,
        });
        await db.SaveChangesAsync(ct);

        await hub.Clients.User(userId.ToString()).SendAsync("ReceiveDuty", new { title, body }, ct);
        webPush.QueueForUser(userId, title, body);
    }

    private static DutyDto ToDto(Duty d, Guid me, DateTime nowUtc) => new(
        d.Id, d.AssignerId, d.AssigneeId,
        d.Assigner.FullName, d.Assigner.AvatarUrl,
        d.Assignee.FullName, d.Assignee.AvatarUrl,
        d.Title, d.Note, d.StartsAtUtc, d.RemindBeforeMin, d.DueAtUtc,
        d.AcceptedAtUtc, d.Status, d.Attempt,
        d.CreatedAt, d.SubmittedAtUtc, d.ReviewedAtUtc, d.ReviewNote,
        d.AssignerId == me,
        d.Status != DutyStatuses.Approved && d.Status != DutyStatuses.Declined && nowUtc > d.DueAtUtc,
        d.AcceptedAtUtc is null && d.Status == DutyStatuses.Open,
        d.Media
            .OrderBy(m => m.Attempt).ThenBy(m => m.Id)
            .Select(m => new DutyMediaDto(m.Id, m.Side, m.Kind, m.Text, m.Url, m.DurationMs, m.Attempt))
            .ToList());
}
