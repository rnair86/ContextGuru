namespace ContextGuru.Tests;

/// <summary>A TimeProvider whose clock is set by the test.</summary>
public sealed class FixedTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}
