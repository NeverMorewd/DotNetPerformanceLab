namespace DotNetPerformanceLab;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            if (args.Length > 0 && string.Equals(args[0], "publish", StringComparison.OrdinalIgnoreCase))
            {
                return await RepositoryPublisher.RunAsync(cancellation.Token).ConfigureAwait(false);
            }

            if (args.Length > 0 && string.Equals(args[0], "summary", StringComparison.OrdinalIgnoreCase))
            {
                return await AppendJobSummaryAsync(cancellation.Token).ConfigureAwait(false);
            }

            if (args.Length > 0 && string.Equals(args[0], "site", StringComparison.OrdinalIgnoreCase))
            {
                return await GitHubPagesSiteBuilder.RunAsync(cancellation.Token).ConfigureAwait(false);
            }

            if (args.Length > 0 && string.Equals(args[0], "compare", StringComparison.OrdinalIgnoreCase))
            {
                return await ComparisonRunner.RunAsync(cancellation.Token).ConfigureAwait(false);
            }

            var settings = SettingsLoader.FromEnvironment();
            var result = await new PerformanceRunner().RunAsync(settings, cancellation.Token).ConfigureAwait(false);
            Console.WriteLine($"Performance report: {Path.Combine(settings.OutputDirectory, "report.md")}");
            Console.WriteLine($"Completed {result.Iterations.Count} baseline iteration(s).");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Performance analysis was cancelled.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(".NET Performance Lab");
        Console.WriteLine("Configure the run through DPL_* environment variables. DPL_TARGET_PATH is required.");
        Console.WriteLine("See the repository documentation for the complete workflow contract.");
    }

    private static async Task<int> AppendJobSummaryAsync(CancellationToken cancellationToken)
    {
        var reportPath = Environment.GetEnvironmentVariable("DPL_REPORT_PATH");
        var summaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (string.IsNullOrWhiteSpace(reportPath) || string.IsNullOrWhiteSpace(summaryPath))
        {
            Console.Error.WriteLine("DPL_REPORT_PATH and GITHUB_STEP_SUMMARY are required.");
            return 1;
        }

        var report = await File.ReadAllTextAsync(reportPath, cancellationToken).ConfigureAwait(false);
        await File.AppendAllTextAsync(summaryPath, report + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
