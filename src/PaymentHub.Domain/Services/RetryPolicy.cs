namespace PaymentHub.Domain.Services;

public static class RetryPolicy
{
    public static readonly TimeSpan[] Schedule =
    {
        TimeSpan.Zero,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1)
    };

    public const int MaxAttempts = 5;

    public static DateTime? NextRetryAt(int retryCount, DateTime now)
    {
        if (retryCount < 0) return now;
        if (retryCount >= Schedule.Length) return null;
        return now.Add(Schedule[retryCount]);
    }

    public static bool IsExhausted(int retryCount) => retryCount >= MaxAttempts;
}
