using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Retune.Core.Parental;

namespace Retune.Core.Tests.Parental;

public sealed class PinTests
{
    [Theory]
    [InlineData("1234", true)]
    [InlineData("12345678", true)]
    [InlineData("123", false)]
    [InlineData("12a4", false)]
    [InlineData("123456789", false)]
    [InlineData("١٢٣٤", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Pin_format_is_4_to_8_ascii_digits(string? pin, bool valid)
    {
        Assert.Equal(valid, PinHash.IsValidFormat(pin));
    }

    [Fact]
    public void Hashes_are_salted_and_verify()
    {
        var a = PinHash.Create("2468");
        var b = PinHash.Create("2468");

        Assert.NotEqual(a, b);
        Assert.StartsWith("pbkdf2-sha256$310000$", a, StringComparison.Ordinal);
        Assert.Equal(PinCheck.Correct, PinHash.Verify("2468", a));
        Assert.Equal(PinCheck.Wrong, PinHash.Verify("2469", a));
        Assert.Equal(PinCheck.Wrong, PinHash.Verify("2468", null));
    }

    [Theory]
    [InlineData("pbkdf2-sha256$0$00$00")]
    [InlineData("pbkdf2-sha256$x$y$z")]
    [InlineData("pbkdf2-sha256$1000$abc$00")]
    [InlineData("pbkdf2-sha256$1000$$00")]
    [InlineData("pbkdf2-sha256$1000$00")]
    [InlineData("pbkdf2-sha256$1000$zz$00")]
    [InlineData("pbkdf2-sha256$2000000000$00$00")]
    public void Malformed_hashes_never_verify(string stored)
    {
        Assert.Equal(PinCheck.Wrong, PinHash.Verify("1234", stored));
    }

    [Fact]
    public void Legacy_hashes_verify_and_are_flagged_for_upgrade()
    {
        var legacy = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("lumen.parental.1234")));

        Assert.Equal(PinCheck.CorrectLegacy, PinHash.Verify("1234", legacy));
        Assert.Equal(PinCheck.Wrong, PinHash.Verify("4321", legacy));
    }

    [Fact]
    public void Attempts_lock_out_after_five_failures_with_growing_delays()
    {
        var time = new FakeTimeProvider();
        var limiter = new PinAttemptLimiter(time);

        for (var i = 0; i < 4; i++)
        {
            limiter.RecordFailure();
            Assert.True(limiter.CanAttempt);
        }

        limiter.RecordFailure();
        Assert.False(limiter.CanAttempt);
        Assert.Equal(TimeSpan.FromSeconds(30), limiter.RemainingLockout);

        time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(limiter.CanAttempt);
        limiter.RecordFailure();
        Assert.Equal(TimeSpan.FromMinutes(1), limiter.RemainingLockout);

        time.Advance(TimeSpan.FromMinutes(1));
        limiter.RecordFailure();
        time.Advance(TimeSpan.FromMinutes(5));
        limiter.RecordFailure();
        Assert.Equal(TimeSpan.FromMinutes(5), limiter.RemainingLockout);

        limiter.RecordSuccess();
        Assert.True(limiter.CanAttempt);
    }
}
