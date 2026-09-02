using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;

namespace DotNetPerformanceLab;

public sealed record LiveMetricBatch(
    int SchemaVersion,
    string RunId,
    long Sequence,
    DateTimeOffset SentUtc,
    IReadOnlyList<MetricSample> Metrics,
    bool Completed);

public interface ILiveMetricPublisher : IAsyncDisposable
{
    bool Enabled { get; }
    long DroppedBatches { get; }
    void TryPublish(IReadOnlyList<MetricSample> metrics);
}

public sealed class LiveMetricPublisher : ILiveMetricPublisher
{
    private const int QueueCapacity = 128;
    private readonly Channel<IReadOnlyList<MetricSample>> _channel;
    private readonly HttpClient _client;
    private readonly Uri _publishUri;
    private readonly string _runId;
    private readonly Task _worker;
    private long _sequence;
    private long _droppedBatches;
    private Exception? _failure;

    private LiveMetricPublisher(Uri endpoint, string token, string runId)
    {
        _runId = runId;
        _publishUri = new Uri(endpoint.ToString().TrimEnd('/') + $"/runs/{Uri.EscapeDataString(runId)}/metrics", UriKind.Absolute);
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("DotNetPerformanceLab/2.0");
        _channel = Channel.CreateBounded<IReadOnlyList<MetricSample>>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        _worker = RunAsync();
    }

    public bool Enabled => true;
    public long DroppedBatches => Interlocked.Read(ref _droppedBatches);

    public static ILiveMetricPublisher Create(RunSettings settings) =>
        settings.LiveEndpoint is null
            ? DisabledLiveMetricPublisher.Instance
            : new LiveMetricPublisher(settings.LiveEndpoint, settings.LiveToken!, settings.LiveRunId!);

    public void TryPublish(IReadOnlyList<MetricSample> metrics)
    {
        if (!_channel.Writer.TryWrite(metrics)) Interlocked.Increment(ref _droppedBatches);
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
        try
        {
            await SendAsync([], completed: true).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _failure ??= exception;
        }
        _client.Dispose();
        if (_failure is not null)
        {
            Console.Error.WriteLine($"Live metric publishing stopped: {_failure.Message}");
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var metrics in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await SendAsync(metrics, completed: false).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _failure = exception;
            while (_channel.Reader.TryRead(out _)) Interlocked.Increment(ref _droppedBatches);
        }
    }

    private async Task SendAsync(IReadOnlyList<MetricSample> metrics, bool completed)
    {
        var batch = new LiveMetricBatch(1, _runId, Interlocked.Increment(ref _sequence), DateTimeOffset.UtcNow, metrics, completed);
        using var response = await _client.PostAsJsonAsync(_publishUri, batch, LabJsonContext.Default.LiveMetricBatch).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private sealed class DisabledLiveMetricPublisher : ILiveMetricPublisher
    {
        public static DisabledLiveMetricPublisher Instance { get; } = new();
        public bool Enabled => false;
        public long DroppedBatches => 0;
        public void TryPublish(IReadOnlyList<MetricSample> metrics) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
