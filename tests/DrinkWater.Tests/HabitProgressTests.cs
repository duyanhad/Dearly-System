using DrinkWater.Api.Entities;

namespace DrinkWater.Tests;

/// <summary>
/// Bữa ăn / uống thuốc chỉ tính ca chính; bữa phụ, ăn vặt và lần ghi không chọn đúng ca không cộng %.
/// Hàm này dùng chung cho màn Hôm nay, trang partner, chuỗi thành tích, lịch và phần thưởng.
/// </summary>
public class HabitProgressTests
{
    private static (string? Slot, string Status) A(string? slot) => (slot, "approved");
    private static (string? Slot, string Status) P(string? slot) => (slot, "pending");

    [Fact]
    public void Snack_and_extra_do_not_count()
    {
        Assert.Equal(0, HabitProgress.Done("meal", [A("snack"), A("extra")]));
    }

    [Fact]
    public void Only_main_meals_count()
    {
        Assert.Equal(2, HabitProgress.Done("meal", [A("breakfast"), A("lunch"), A("snack"), A("extra")]));
    }

    [Fact]
    public void Same_meal_reported_twice_counts_once()
    {
        Assert.Equal(1, HabitProgress.Done("meal", [A("lunch"), A("lunch")]));
    }

    [Fact]
    public void Missing_or_unknown_slot_does_not_count()
    {
        Assert.Equal(0, HabitProgress.Done("meal", [A(null), A("brunch")]));
    }

    [Fact]
    public void Pending_does_not_count_but_approval_does()
    {
        Assert.Equal(0, HabitProgress.Done("meal", [P("lunch")]));
        Assert.Equal(1, HabitProgress.Done("meal", [A("lunch")]));
    }

    [Fact]
    public void Medicine_counts_its_own_slots()
    {
        Assert.Equal(3, HabitProgress.Done("medicine", [A("morning"), A("noon"), A("night")]));
        Assert.Equal(0, HabitProgress.Done("medicine", [A("lunch")]));
    }

    [Fact]
    public void Study_still_counts_every_approved_log()
    {
        Assert.Equal(2, HabitProgress.Done("study", [A(null), A(null), P(null)]));
    }

    [Fact]
    public void CountsForGoal_rejects_unknown_slots_for_slot_kinds()
    {
        Assert.True(HabitProgress.CountsForGoal("meal", "dinner"));
        Assert.False(HabitProgress.CountsForGoal("meal", "snack"));
        Assert.False(HabitProgress.CountsForGoal("meal", null));
        Assert.True(HabitProgress.CountsForGoal("study", null));
    }
}
