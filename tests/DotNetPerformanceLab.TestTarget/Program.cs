using System.Diagnostics;

var lifetimeSeconds = 120;
for (var index = 0; index < args.Length - 1; index++)
{
    if (args[index] == "--lifetime-seconds" && int.TryParse(args[index + 1], out var value))
    {
        lifetimeSeconds = value;
    }
}

var retained = new byte[4 * 1024 * 1024];
var stopwatch = Stopwatch.StartNew();
while (stopwatch.Elapsed < TimeSpan.FromSeconds(lifetimeSeconds))
{
    for (var index = 0; index < retained.Length; index += 4096)
    {
        retained[index]++;
    }

    _ = Enumerable.Range(0, 1000).Select(value => value.ToString()).ToArray();
    await Task.Delay(25);
}
