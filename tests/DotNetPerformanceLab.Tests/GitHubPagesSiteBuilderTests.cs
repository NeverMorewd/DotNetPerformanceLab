using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DotNetPerformanceLab.Tests;

public sealed class GitHubPagesSiteBuilderTests : IDisposable
{
    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), $"dpl-pages-{Guid.NewGuid():N}");

    [Fact]
    public async Task BuildAsyncPublishesUnexpiredReportsAndHistoryIndex()
    {
        using var client = new HttpClient(new GitHubApiHandler(CreateArtifactArchive()))
        {
            BaseAddress = new Uri("https://api.github.test/")
        };
        var settings = new GitHubPagesSiteSettings("owner/repository", _outputDirectory, "dotnet-performance-", 30, 10);

        var exitCode = await GitHubPagesSiteBuilder.BuildAsync(client, settings, TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(_outputDirectory, "runs", "456", "123", "index.html")));
        var index = await File.ReadAllTextAsync(Path.Combine(_outputDirectory, "index.html"), TestContext.Current.CancellationToken);
        Assert.Contains("Test application", index, StringComparison.Ordinal);
        Assert.Contains("runs/456/123/index.html", index, StringComparison.Ordinal);
        Assert.Contains("runs/456/123/comparison-data.json", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsyncRejectsArtifactPathTraversal()
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("web-report/../../outside.txt");
        }

        using var client = new HttpClient(new GitHubApiHandler(archive.ToArray()))
        {
            BaseAddress = new Uri("https://api.github.test/")
        };
        var settings = new GitHubPagesSiteSettings("owner/repository", _outputDirectory, "dotnet-performance-", 30, 10);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            GitHubPagesSiteBuilder.BuildAsync(client, settings, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildAsyncPublishesComparisonArtifacts()
    {
        using var client = new HttpClient(new GitHubApiHandler(CreateComparisonArtifactArchive())) { BaseAddress = new Uri("https://api.github.test/") };
        var settings = new GitHubPagesSiteSettings("owner/repository", _outputDirectory, "dotnet-performance-", 30, 10);

        var exitCode = await GitHubPagesSiteBuilder.BuildAsync(client, settings, TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var index = await File.ReadAllTextAsync(Path.Combine(_outputDirectory, "index.html"), TestContext.Current.CancellationToken);
        Assert.Contains("Comparison", index, StringComparison.Ordinal);
        Assert.Contains("Before vs After", index, StringComparison.Ordinal);
        Assert.Contains("2 reports", index, StringComparison.Ordinal);
    }

    private static byte[] CreateArtifactArchive()
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "web-report/index.html", "<!doctype html><title>Report</title>");
            WriteEntry(zip, "summary.json", JsonSerializer.Serialize(CreateResult()));
        }

        return archive.ToArray();
    }

    private static byte[] CreateComparisonArtifactArchive()
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "web-report/index.html", "<!doctype html><title>Comparison</title>");
            WriteEntry(zip, "comparison-summary.json", JsonSerializer.Serialize(new ComparisonReportSummary(1, "Before vs After", DateTimeOffset.UtcNow, ["Before", "After"], 12)));
        }

        return archive.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string contents)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }

    private static PerformanceRunResult CreateResult() => new(
        2,
        "test-app",
        "Test application",
        new RunSettingsSnapshot(1, 5, 1000, 0, 1, false, 30, false, 10),
        new EnvironmentSnapshot("Test OS", ".NET", "X64", "X64", 8, "test", "test", "Test OS", "X64", DateTimeOffset.UtcNow),
        [],
        new DiagnosticArtifact("Runtime counters", false, false, null, null),
        new DiagnosticArtifact("EventPipe trace", false, false, null, null),
        [],
        DateTimeOffset.UtcNow,
        []);

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    private sealed class GitHubApiHandler(byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/actions/artifacts", StringComparison.Ordinal))
            {
                var timestamp = DateTimeOffset.UtcNow;
                var json = JsonSerializer.Serialize(new
                {
                    artifacts = new[]
                    {
                        new
                        {
                            id = 123,
                            name = "dotnet-performance-test",
                            archive_download_url = "https://api.github.test/artifacts/123.zip",
                            expired = false,
                            created_at = timestamp,
                            expires_at = timestamp.AddDays(30),
                            workflow_run = new { id = 456 }
                        }
                    }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            if (request.RequestUri.AbsolutePath == "/artifacts/123.zip")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archive)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
