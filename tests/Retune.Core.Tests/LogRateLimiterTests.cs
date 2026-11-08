using Microsoft.Extensions.Time.Testing;
using Retune.Core.Diagnostics;

namespace Retune.Core.Tests;

public sealed class LogRateLimiterTests
{
    [Fact]
    public void Lets_a_burst_through_up_to_the_limit_then_reports_what_was_dropped()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var limiter = new LogRateLimiter(3, TimeSpan.FromMinutes(1), time);

        var allowed = Enumerable.Range(0, 10).Count(_ => limiter.Allow(out _));
        Assert.Equal(3, allowed);

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(limiter.Allow(out var dropped));
        Assert.Equal(7, dropped);
        Assert.True(limiter.Allow(out var none));
        Assert.Equal(0, none);
    }
}
