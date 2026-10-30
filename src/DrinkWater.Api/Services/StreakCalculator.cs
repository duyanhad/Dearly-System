namespace DrinkWater.Api.Services;

public static class StreakCalculator
{
    /// <summary>
    /// Recomputes the streak for an ordered run of days. A day with no row at all is a gap
    /// and breaks the chain, so callers must pass every day that has a summary, not a
    /// contiguous range. <paramref name="anchorDate"/>/<paramref name="anchorStreak"/> is the
    /// day before the first passed day, when it exists.
    /// </summary>
    public static List<(DateOnly Date, int Streak)> Compute(
        IReadOnlyList<(DateOnly Date, bool IsCompleted)> orderedDays,
        DateOnly? anchorDate,
        int anchorStreak)
    {
        var result = new List<(DateOnly, int)>(orderedDays.Count);
        var streak = anchorStreak;
        var previousDate = anchorDate;

        foreach (var (date, isCompleted) in orderedDays)
        {
            if (!isCompleted)
                streak = 0;
            else if (previousDate.HasValue && streak > 0 && date == previousDate.Value.AddDays(1))
                streak++;
            else
                streak = 1;

            result.Add((date, streak));
            previousDate = date;
        }

        return result;
    }
}
