using System.Globalization;
using Retune.FakeProvider;

// dotnet run --project tools/FakeProvider -- [--port 8080] [--movies 65000] [--channels 16000] [--series 7000] [--media <folder>] [--no-range]
string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

int Number(string name, int fallback) => int.TryParse(Arg(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

var defaults = new FakeProviderOptions();
var media = Arg("--media") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "fixtures", "media"));
var options = defaults with
{
    Channels = Number("--channels", defaults.Channels),
    Movies = Number("--movies", defaults.Movies),
    Series = Number("--series", defaults.Series),
    MediaFolder = Directory.Exists(media) ? media : null,
    RangeRequests = !args.Contains("--no-range"),
};

await using var server = await FakeProviderServer.StartAsync(options, Number("--port", 8080));
Console.WriteLine($"Fake provider at {server.BaseUrl}  (username: {options.Username}, password: {options.Password})");
Console.WriteLine($"{options.Channels} channels, {options.Movies} movies, {options.Series} series. Press Ctrl+C to stop.");
var stop = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.TrySetResult();
};
await stop.Task;
