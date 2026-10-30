using DrinkWater.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Data;

/// <summary>Seed 3 kỷ nguyên mặc định (khớp bộ ảnh đã có trong public/mascots) khi DB còn trống.</summary>
public static class EraSeeder
{
    public static async Task SeedErasAsync(AppDbContext db, ILogger logger)
    {
        if (await db.Eras.AnyAsync())
            return;

        var eras = new List<Era>
        {
            Build("Kỷ nguyên Vũ khí Cận chiến", "Kiếm, giáo, khiên nước — sức mạnh thuần khiết",
                "#f4fbff", "#aedcf0", 0, 1, "era1",
                [
                    ("Linh Hồn Nước", "Nhỏ bé nhưng đầy năng lượng!", "Sức mạnh cơ bản", 20),
                    ("Kiếm Nước", "Vững vàng hơn, sẵn sàng chiến đấu!", "Kiếm nước", 60),
                    ("Pháp Trượng Nước", "Tri thức và sức mạnh hòa quyện!", "Pháp trượng", 150),
                    ("Chiến Binh Nước", "Bứt phá giới hạn, chinh phục thử thách!", "Kiếm & Khiên Nước", 350),
                    ("Thần Nước", "Lan tỏa hy vọng, kiến tạo tương lai!", "Thần khí Nước", 800)
                ]),

            Build("Kỷ nguyên Phép thuật", "Trượng phép, sách ma pháp — sức mạnh của tri thức",
                "#eef7ff", "#9dc8f5", 300, 2, "era2",
                [
                    ("Tập Sự Pháp Sư", "Bắt đầu hành trình làm chủ sức mạnh nước!", "Cây trượng nước cơ bản", 40),
                    ("Pháp Sư Học Việc", "Làm chủ những phép thuật cơ bản!", "Trượng phép cấp 1 + Sách ma pháp", 120),
                    ("Pháp Sư Tinh Anh", "Kết nối với dòng chảy ma thuật cổ xưa!", "Trượng phép cấp 2 + Sách ma thuật", 300),
                    ("Pháp Sư Đại Tài", "Điều khiển nguyên tố nước, áp đảo mọi thử thách!", "Trượng phép cấp 3 + Quyển sách tri thức", 600),
                    ("Đại Pháp Sư", "Sức mạnh nước vô tận, kiến tạo thế giới tốt đẹp hơn!", "Trượng Huyền Thoại + Sách Cổ Đại", 1200)
                ]),

            Build("Kỷ nguyên Thần thánh", "Hào quang, đôi cánh — bước tới cõi thần",
                "#f7fcff", "#c9e9ff", 900, 3, "era1",
                [
                    ("Khởi Nguyên", "Nhỏ bé nhưng đầy năng lượng!", "Sức mạnh cơ bản", 80),
                    ("Hồi Sinh", "Thêm sức mạnh, lan toả năng lượng!", "Năng lượng nước", 240),
                    ("Toả Sáng", "Kết nối năng lượng, lan toả giá trị!", "Vòng năng lượng", 600),
                    ("Thức Tỉnh", "Bản lĩnh lớn hơn, lan xa hơn!", "Hào quang", 1200),
                    ("Thần Thánh", "Vượt giới hạn, kiến tạo tương lai!", "Thần khí tối thượng", 2400)
                ])
        };

        db.Eras.AddRange(eras);
        await db.SaveChangesAsync();
        logger.LogInformation("Đã seed {Count} kỷ nguyên linh vật", eras.Count);
    }

    /// <summary>
    /// Tạo tiền mặc định "Giọt" (gắn cột User.Coins) và nhóm “Nước” nếu chưa có,
    /// rồi gán mọi kỷ nguyên chưa thuộc nhóm nào vào đó.
    /// </summary>
    public static async Task SeedDefaultGroupAsync(AppDbContext db, ILogger logger)
    {
        if (!await db.Currencies.AnyAsync(c => c.IsDefault))
        {
            db.Currencies.Add(new Currency
            {
                Code = "drops",
                Name = "Giọt",
                IconKey = "droplet",
                Color = "#f0b429",
                IsDefault = true,
                Order = 1,
            });
            await db.SaveChangesAsync();
        }

        if (!await db.EraGroups.AnyAsync(g => g.Slug == "water"))
        {
            db.EraGroups.Add(new EraGroup { Name = "Nước", Slug = "water", Color = "#aedcf0", Order = 1 });
        }

        await db.SaveChangesAsync();

        var waterId = await db.EraGroups.Where(g => g.Slug == "water").Select(g => g.Id).FirstAsync();
        var orphan = await db.Eras.Where(e => e.GroupId == null).ToListAsync();
        if (orphan.Count == 0) return;

        foreach (var era in orphan) era.GroupId = waterId;
        await db.SaveChangesAsync();
        logger.LogInformation("Đã gán {Count} kỷ nguyên vào nhóm Nước", orphan.Count);
    }

    private static Era Build(
        string title, string subtitle, string bgFrom, string bgTo,
        int unlockPrice, int order, string imageFolder,
        (string Name, string Desc, string Artifact, int Price)[] levels)
    {
        var era = new Era
        {
            Order = order,
            Title = title,
            Subtitle = subtitle,
            BgFrom = bgFrom,
            BgTo = bgTo,
            UnlockPrice = unlockPrice,
            IsActive = true
        };

        for (var i = 0; i < levels.Length; i += 1)
        {
            var (name, desc, artifact, price) = levels[i];
            era.Levels.Add(new EraLevel
            {
                Level = i + 1,
                Name = name,
                Description = desc,
                Artifact = artifact,
                Price = price,
                ImageUrl = $"/mascots/{imageFolder}/{i + 1}.webp"
            });
        }

        return era;
    }
}
