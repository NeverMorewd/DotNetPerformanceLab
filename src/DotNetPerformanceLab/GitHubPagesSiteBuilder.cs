using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DotNetPerformanceLab;

public static class GitHubPagesSiteBuilder
{
    private const int MaximumApiArtifacts = 100;

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var repository = Require("DPL_SITE_REPOSITORY");
        var token = Require("GITHUB_TOKEN");
        var outputDirectory = Path.GetFullPath(Require("DPL_SITE_OUTPUT_DIRECTORY"));
        var apiUrl = Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com";
        var prefix = Environment.GetEnvironmentVariable("DPL_SITE_ARTIFACT_PREFIX") ?? "dotnet-performance-";
        var historyDays = ReadInteger("DPL_SITE_HISTORY_DAYS", 30, 1, 400);
        var maximumRuns = ReadInteger("DPL_SITE_MAXIMUM_RUNS", 50, 1, 100);

        using var client = new HttpClient { BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/", UriKind.Absolute) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DotNetPerformanceLab/2.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        return await BuildAsync(
            client,
            new GitHubPagesSiteSettings(repository, outputDirectory, prefix, historyDays, maximumRuns),
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<int> BuildAsync(
        HttpClient client,
        GitHubPagesSiteSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSettings(settings);
        PrepareOutputDirectory(settings.OutputDirectory);

        var artifacts = await GetArtifactsAsync(
            client,
            settings.Repository,
            settings.ArtifactPrefix,
            settings.HistoryDays,
            settings.MaximumRuns,
            cancellationToken).ConfigureAwait(false);
        var reports = new List<SiteReport>(artifacts.Count);
        foreach (var artifact in artifacts)
        {
            var report = await DownloadReportAsync(client, artifact, settings.OutputDirectory, cancellationToken).ConfigureAwait(false);
            if (report is not null)
            {
                reports.Add(report);
            }
        }

        await WriteIndexAsync(settings.OutputDirectory, settings.Repository, reports, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Built GitHub Pages site with {reports.Count} report(s).");
        return 0;
    }

    private static async Task<IReadOnlyList<GitHubArtifact>> GetArtifactsAsync(
        HttpClient client,
        string repository,
        string prefix,
        int historyDays,
        int maximumRuns,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"repos/{repository}/actions/artifacts?per_page={MaximumApiArtifacts}",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-historyDays);
        return document.RootElement.GetProperty("artifacts").EnumerateArray()
            .Select(ParseArtifact)
            .Where(artifact => !artifact.Expired && artifact.Name.StartsWith(prefix, StringComparison.Ordinal) && artifact.CreatedUtc >= cutoff)
            .OrderByDescending(artifact => artifact.CreatedUtc)
            .Take(maximumRuns)
            .ToArray();
    }

    private static GitHubArtifact ParseArtifact(JsonElement element) => new(
        element.GetProperty("id").GetInt64(),
        element.GetProperty("name").GetString() ?? string.Empty,
        element.GetProperty("archive_download_url").GetString() ?? string.Empty,
        element.GetProperty("expired").GetBoolean(),
        element.GetProperty("created_at").GetDateTimeOffset(),
        element.GetProperty("expires_at").GetDateTimeOffset(),
        element.TryGetProperty("workflow_run", out var run) && run.TryGetProperty("id", out var runId) ? runId.GetInt64() : 0);

    private static async Task<SiteReport?> DownloadReportAsync(
        HttpClient client,
        GitHubArtifact artifact,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(artifact.DownloadUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var archiveStream = new MemoryStream();
        await response.Content.CopyToAsync(archiveStream, cancellationToken).ConfigureAwait(false);
        archiveStream.Position = 0;
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
        var reportRoot = Path.GetFullPath(Path.Combine(outputDirectory, "runs", artifact.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture), artifact.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var webPrefix = "web-report/";
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(webPrefix, StringComparison.Ordinal) && !string.IsNullOrEmpty(entry.Name)))
        {
            var relativePath = entry.FullName[webPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(reportRoot, relativePath));
            var relativeToRoot = Path.GetRelativePath(reportRoot, destination);
            if (Path.IsPathRooted(relativeToRoot) || relativeToRoot == ".." || relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Artifact {artifact.Id} contains an unsafe path.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var target = File.Create(destination);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        var reportEntry = archive.GetEntry("summary.json");
        var indexPath = Path.Combine(reportRoot, "index.html");
        if (reportEntry is null || !File.Exists(indexPath))
        {
            return null;
        }

        await using var reportStream = reportEntry.Open();
        var result = await JsonSerializer.DeserializeAsync(reportStream, LabJsonContext.Default.PerformanceRunResult, cancellationToken).ConfigureAwait(false);
        return result is null
            ? null
            : new SiteReport(
                artifact.Id,
                artifact.RunId,
                artifact.Name,
                result.ReportLabel,
                result.GeneratedUtc,
                artifact.ExpiresUtc,
                result.Environment.OperatingSystem,
                result.Environment.ProcessArchitecture,
                $"runs/{artifact.RunId}/{artifact.Id}/index.html");
    }

    private static async Task WriteIndexAsync(
        string outputDirectory,
        string repository,
        IReadOnlyList<SiteReport> reports,
        CancellationToken cancellationToken)
    {
        var rows = new StringBuilder();
        foreach (var report in reports)
        {
            rows.Append("<tr><td><a href=\"").Append(Html(report.RelativeUrl)).Append("\">")
                .Append(Html(report.Label)).Append("</a></td><td>")
                .Append(Html(report.OperatingSystem)).Append(" · ").Append(Html(report.Architecture))
                .Append("</td><td>").Append(report.GeneratedUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture))
                .Append("</td><td>").Append(report.ExpiresUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine("</td></tr>");
        }

        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>.NET Performance History</title>
              <style>
                :root{color-scheme:dark;font-family:Inter,system-ui,sans-serif;background:#080d16;color:#eef4ff}body{margin:0}main{width:min(1180px,calc(100% - 32px));margin:0 auto;padding:42px 0}h1{font-size:clamp(28px,4vw,44px);margin:0}.sub{color:#8ea4c8;margin:8px 0 28px}table{width:100%;border-collapse:separate;border-spacing:0;background:#0e1624;border:1px solid #1e2d49;border-radius:16px;overflow:hidden}th,td{text-align:left;padding:14px 16px;border-bottom:1px solid #1e2d49}th{color:#8ea4c8;font-size:12px;text-transform:uppercase}tr:last-child td{border-bottom:0}a{color:#6ecbff;text-decoration:none}a:hover{text-decoration:underline}.empty{padding:30px;background:#0e1624;border:1px solid #1e2d49;border-radius:16px}
              </style>
            </head>
            <body><main><h1>.NET Performance History</h1><div class="sub">{{Html(repository)}} · reports are removed when their source artifacts expire.</div>
            {{(reports.Count == 0 ? "<div class=\"empty\">No unexpired performance reports are available.</div>" : $"<table><thead><tr><th>Target</th><th>Environment</th><th>Generated</th><th>Expires</th></tr></thead><tbody>{rows}</tbody></table>")}}
            </main></body></html>
            """;
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "index.html"), html, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    private static void PrepareOutputDirectory(string outputDirectory)
    {
        if (Directory.Exists(outputDirectory))
        {
            if (Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            {
                throw new InvalidOperationException($"The site output directory must be empty: {outputDirectory}");
            }
        }
        else
        {
            Directory.CreateDirectory(outputDirectory);
        }
    }

    private static void ValidateSettings(GitHubPagesSiteSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Repository) || settings.Repository.Count(character => character == '/') != 1)
        {
            throw new ArgumentException("Repository must use the owner/name format.", nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(settings.ArtifactPrefix))
        {
            throw new ArgumentException("Artifact prefix cannot be empty.", nameof(settings));
        }

        if (settings.HistoryDays is < 1 or > 400 || settings.MaximumRuns is < 1 or > MaximumApiArtifacts)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "History days or maximum runs are outside the supported range.");
        }
    }

    private static string Require(string name) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
            ? throw new ArgumentException($"Environment variable {name} is required.")
            : Environment.GetEnvironmentVariable(name)!.Trim();

    private static int ReadInteger(string name, int fallback, int minimum, int maximum)
    {
        var text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        return int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= minimum && value <= maximum
            ? value
            : throw new ArgumentOutOfRangeException(name, $"{name} must be from {minimum} to {maximum}.");
    }

    private static string Html(string value) => System.Net.WebUtility.HtmlEncode(value);

    private sealed record GitHubArtifact(long Id, string Name, string DownloadUrl, bool Expired, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, long RunId);
    private sealed record SiteReport(long Id, long RunId, string ArtifactName, string Label, DateTimeOffset GeneratedUtc, DateTimeOffset ExpiresUtc, string OperatingSystem, string Architecture, string RelativeUrl);
}

public sealed record GitHubPagesSiteSettings(
    string Repository,
    string OutputDirectory,
    string ArtifactPrefix,
    int HistoryDays,
    int MaximumRuns);
