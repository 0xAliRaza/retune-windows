using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Retune.Core.Playback;
using Retune.Infrastructure.Net;
using Retune.Mpv.Tests.Support;

namespace Retune.Mpv.Tests;

/// <summary>
/// Real mpv against a local HTTPS server whose certificate nobody trusts (self-signed, made per run): with Windows'
/// roots it must refuse to connect, and play once the user allows unverified streams.
/// </summary>
public sealed class TlsVerificationTests : IAsyncLifetime
{
    private readonly string _bundle = Path.Combine(Path.GetTempPath(), $"retune-roots-{Guid.NewGuid():N}.pem");
    private readonly UiThread _ui = new();
    private WebApplication _server = null!;
    private string _url = null!;

    public async ValueTask InitializeAsync()
    {
        Assert.True(SystemTrustStore.WriteBundle(_bundle) > 0);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, o => o.UseHttps(SelfSigned())));
        _server = builder.Build();
        var media = TestMedia.PathOf(TestMedia.NoAudio);
        _server.MapGet("/movie.mp4", () => Results.File(media, "video/mp4", enableRangeProcessing: true));
        await _server.StartAsync(TestContext.Current.CancellationToken);
        var address = _server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _url = address.Replace("[::1]", "127.0.0.1", StringComparison.Ordinal) + "/movie.mp4";
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        _ui.Dispose();
        File.Delete(_bundle);
    }

    [Fact]
    public async Task An_untrusted_certificate_is_refused_and_reported_as_such()
    {
        var events = await PlayAsync(verify: true, until: e => e is EngineCertificateRejected or EngineFailed or EngineFirstFrame);

        Assert.Contains(events, e => e is EngineCertificateRejected);
        Assert.DoesNotContain(events, e => e is EngineFirstFrame);
    }

    [Fact]
    public async Task Allowing_unverified_streams_plays_it()
    {
        var events = await PlayAsync(verify: false, until: e => e is EngineFirstFrame or EngineFailed or EngineCertificateRejected);

        Assert.Contains(events, e => e is EngineFirstFrame);
    }

    private async Task<IReadOnlyCollection<EngineEvent>> PlayAsync(bool verify, Func<EngineEvent, bool> until)
    {
        var options = MpvOptions.ForEmbeddedPlayer(null, 100, "Retune-Tests/1.0", silent: true, trustedCertificates: _bundle);
        using var mpv = MpvClient.Create(options, logLevel: "warn");
        var engine = _ui.Invoke(() => new MpvEngine(mpv, _ui.Post));
        var events = new ConcurrentQueue<EngineEvent>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Invoke(() =>
        {
            engine.EventRaised += (_, e) =>
            {
                events.Enqueue(e);
                if (until(e))
                {
                    done.TrySetResult();
                }
            };
            engine.SetVerifyCertificates(verify);
            engine.Load(_url, live: false, startSeconds: null);
        });

        await done.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        _ui.Invoke(engine.Dispose);
        return events;
    }

    private static X509Certificate2 SelfSigned()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }
}
