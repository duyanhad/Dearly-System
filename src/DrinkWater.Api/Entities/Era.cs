namespace DrinkWater.Api.Entities;

/// <summary>Kỷ nguyên linh vật: admin tự tạo, người dùng mở bằng "giọt" kiếm từ chuỗi ngày.</summary>
public class Era
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Thứ tự hiển thị (1, 2, 3…). Kỷ nguyên đầu tiên mở sẵn cho mọi người.</summary>
    public int Order { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Màu nền thẻ hiển thị (gradient từ BgFrom → BgTo).</summary>
    public string BgFrom { get; set; } = "#f4fbff";
    public string BgTo { get; set; } = "#aedcf0";

    /// <summary>Giá mở khoá kỷ nguyên (giọt).</summary>
    public int UnlockPrice { get; set; } = 150;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Nhóm kỷ nguyên (Nước / Uống thuốc / Ăn / Ngủ…) — null = chưa xếp nhóm.</summary>
    public Guid? GroupId { get; set; }
    public EraGroup? Group { get; set; }

    public List<EraLevel> Levels { get; set; } = [];
}

/// <summary>
/// Nhóm kỷ nguyên. Sau này mỗi loại nhắc nhở (uống nước / uống thuốc / ăn / ngủ)
/// sẽ tra kỷ nguyên theo <see cref="Slug"/> để lấy đúng bộ linh vật.
/// </summary>
public class EraGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Khoá ổn định cho code/API (water, medicine, meal, sleep…).</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Màu nhãn hiển thị.</summary>
    public string Color { get; set; } = "#aedcf0";

    public int Order { get; set; }

    /// <summary>Đơn vị tiền dùng để mua/nâng cấp kỷ nguyên trong nhóm (null = tiền mặc định).</summary>
    public Guid? CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    /// <summary>Luật phát điểm: số điểm mỗi lần đạt mục tiêu.</summary>
    public int AwardBase { get; set; } = WaterCoins.BaseReward;

    /// <summary>Luật phát điểm: cộng thêm mỗi ngày chuỗi.</summary>
    public int AwardPerStreakDay { get; set; } = WaterCoins.PerStreakDay;
}

/// <summary>
/// Một loại tiền trong game. "Giọt" là tiền mặc định gắn với cột <see cref="User.Coins"/>;
/// các loại khác giữ số dư riêng ở <see cref="UserCurrency"/>.
/// </summary>
public class Currency
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Khoá ổn định (drops, sleep, pill…).</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Tên icon ở frontend: droplet | moon | pill | bowl | star | leaf | heart.</summary>
    public string IconKey { get; set; } = "star";

    public string Color { get; set; } = "#f0b429";

    /// <summary>Tiền mặc định (dùng cột User.Coins). Chỉ nên có một.</summary>
    public bool IsDefault { get; set; }

    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Số dư của người dùng với một loại tiền không mặc định.</summary>
public class UserCurrency
{
    public Guid UserId { get; set; }
    public Guid CurrencyId { get; set; }
    public int Amount { get; set; }
}

/// <summary>Một cấp trong kỷ nguyên (mỗi kỷ nguyên 5 cấp).</summary>
public class EraLevel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EraId { get; set; }

    /// <summary>1..5 trong kỷ nguyên.</summary>
    public int Level { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Tên thần khí / phụ kiện hiển thị dưới tên cấp.</summary>
    public string Artifact { get; set; } = string.Empty;

    /// <summary>Giá nâng lên cấp này (giọt).</summary>
    public int Price { get; set; }

    /// <summary>Ảnh linh vật ở cấp này (đường dẫn upload hoặc /mascots/…).</summary>
    public string? ImageUrl { get; set; }

    public Era Era { get; set; } = null!;
}

/// <summary>Tiến độ mở khoá của một người trong một kỷ nguyên.</summary>
public class UserEraProgress
{
    public Guid UserId { get; set; }
    public Guid EraId { get; set; }

    /// <summary>Cấp cao nhất đã mở (0 = chưa mở cấp nào, 1..5).</summary>
    public int UnlockedLevel { get; set; }

    public DateTime UnlockedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Cách tính "giọt" thưởng khi hoàn thành mục tiêu ngày: chuỗi càng dài càng nhiều.</summary>
public static class WaterCoins
{
    public const int BaseReward = 10;
    public const int PerStreakDay = 3;

    /// <summary>10 giọt cơ bản + 3 giọt cho mỗi ngày chuỗi (streak sau khi tính lại).</summary>
    public static int RewardFor(int streak) => BaseReward + PerStreakDay * Math.Max(0, streak);
}
