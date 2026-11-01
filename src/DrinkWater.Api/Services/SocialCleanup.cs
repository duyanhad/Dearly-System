using DrinkWater.Api.Data;
using DrinkWater.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Services;

/// <summary>
/// Dọn quan hệ partner "mồ côi": đã bỏ kết bạn nhưng vẫn còn trỏ PartnerId/PendingPartnerId vào nhau
/// (dữ liệu tạo trước khi có luật "xoá bạn là mất partner"). Chạy lúc khởi động, idempotent.
/// </summary>
public static class SocialCleanup
{
    public static async Task ClearOrphanPartnersAsync(AppDbContext db)
    {
        var orphanPartner = db.Users.Where(u => u.PartnerId != null &&
            !db.Follows.Any(f => f.Status == FollowStatuses.Accepted &&
                ((f.FollowerId == u.Id && f.FollowingId == u.PartnerId) ||
                 (f.FollowerId == u.PartnerId && f.FollowingId == u.Id))));

        var orphanPending = db.Users.Where(u => u.PendingPartnerId != null &&
            !db.Follows.Any(f => f.Status == FollowStatuses.Accepted &&
                ((f.FollowerId == u.Id && f.FollowingId == u.PendingPartnerId) ||
                 (f.FollowerId == u.PendingPartnerId && f.FollowingId == u.Id))));

        await orphanPartner.ExecuteUpdateAsync(s => s.SetProperty(x => x.PartnerId, (Guid?)null));
        await orphanPending.ExecuteUpdateAsync(s => s.SetProperty(x => x.PendingPartnerId, (Guid?)null));
    }
}