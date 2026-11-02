using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;

namespace DrinkWater.Tests;

public class ReminderCadenceTests
{
    private static DateTime At(int hour, int minute = 0) => new(2026, 10, 26, hour, minute, 0);

    [Fact]
    public void Does_not_remind_before_the_interval()
    {
        Assert.False(ReminderCadence.ShouldRemind(
            At(10), lastDrinkLocal: At(9), lastReminderLocal: null, 120, ReminderStyles.Normal));
    }

    [Fact]
    public void Reminds_after_the_interval()
    {
        Assert.True(ReminderCadence.ShouldRemind(
            At(11, 1), lastDrinkLocal: At(9), lastReminderLocal: null, 120, ReminderStyles.Normal));
    }

    [Fact]
    public void Never_drank_gets_reminded()
    {
        Assert.True(ReminderCadence.ShouldRemind(At(7, 30), null, null, 120, ReminderStyles.Normal));
    }

    [Fact]
    public void Style_sets_the_repeat_gap()
    {
        // Đã nhắc 5 phút trước: ác quỷ (1 phút) và trung bình (3 phút) nhắc tiếp, nhẹ nhàng (10 phút) thì chưa.
        Assert.True(ReminderCadence.ShouldRemind(
            At(11, 5), At(8), At(11), 120, ReminderStyles.Demon));
        Assert.True(ReminderCadence.ShouldRemind(
            At(11, 5), At(8), At(11), 120, ReminderStyles.Normal));
        Assert.False(ReminderCadence.ShouldRemind(
            At(11, 5), At(8), At(11), 120, ReminderStyles.Gentle));
    }

    [Fact]
    public void Quiet_hours_are_silent()
    {
        Assert.False(ReminderCadence.ShouldRemind(
            At(3), At(0), null, 60, ReminderStyles.Demon));
        Assert.False(ReminderCadence.ShouldRemind(
            At(23, 30), At(20), null, 60, ReminderStyles.Demon));
    }

    [Fact]
    public void User_window_overrides_defaults()
    {
        // Khung 22:00–24:00: 22:30 thì nhắc, 21:30 thì im.
        Assert.True(ReminderCadence.ShouldRemind(
            At(22, 30), At(20), null, 60, ReminderStyles.Normal, 22, 24));
        Assert.False(ReminderCadence.ShouldRemind(
            At(21, 30), At(20), null, 60, ReminderStyles.Normal, 22, 24));
        // Khung 5:00–8:00: 6:00 nhắc dù 7:00 của mặc định chưa tới.
        Assert.True(ReminderCadence.ShouldRemind(
            At(6), At(1), null, 60, ReminderStyles.Normal, 5, 8));
    }

    [Fact]
    public void Demon_gets_harsher_when_overdue()
    {
        var early = ReminderCadence.Message(ReminderStyles.Demon, 200, 60);
        var late = ReminderCadence.Message(ReminderStyles.Demon, 200, 180);
        Assert.NotEqual(early.Message, late.Message);
        Assert.Contains("200 ml", late.Message);
    }

    [Fact]
    public void No_reminder_once_the_daily_target_is_reached()
    {
        // 2100/2000 ml: đủ 100% thì dù lâu chưa uống thêm cũng im.
        Assert.False(ReminderCadence.ShouldRemind(
            At(15), At(9), null, 120, ReminderStyles.Normal, drankMl: 2100, targetMl: 2000));
        Assert.False(ReminderCadence.ShouldRemind(
            At(15), At(9), null, 120, ReminderStyles.Demon, drankMl: 2000, targetMl: 2000));
        // Chưa đủ thì vẫn nhắc như cũ.
        Assert.True(ReminderCadence.ShouldRemind(
            At(15), At(9), null, 120, ReminderStyles.Normal, drankMl: 1999, targetMl: 2000));
        // Chưa cấu hình chỉ tiêu (0) thì không chặn gì.
        Assert.True(ReminderCadence.ShouldRemind(
            At(15), At(9), null, 120, ReminderStyles.Normal, drankMl: 5000, targetMl: 0));
    }

    [Fact]
    public void Invalid_interval_never_reminds()
    {
        Assert.False(ReminderCadence.ShouldRemind(At(12), null, null, 5, ReminderStyles.Normal));
    }
}
