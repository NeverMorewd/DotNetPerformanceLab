namespace DotNetPerformanceLab;

public static class TargetValidator
{
    public static RunSettings Validate(RunSettings settings)
    {
        var target = Path.GetFullPath(settings.TargetPath);
        var workingDirectory = Path.GetFullPath(settings.WorkingDirectory);
        var outputDirectory = Path.GetFullPath(settings.OutputDirectory);

        if (!File.Exists(target))
        {
            throw new FileNotFoundException("The target executable does not exist.", target);
        }

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException($"The working directory does not exist: {workingDirectory}");
        }

        var allowedRoots = settings.AllowedRoots.Select(Path.GetFullPath).ToArray();
        if (allowedRoots.Length > 0 && !allowedRoots.Any(root => IsWithin(target, root)))
        {
            throw new UnauthorizedAccessException($"The target executable is outside the configured allowed roots: {target}");
        }

        if (IsWithin(outputDirectory, workingDirectory) && string.Equals(outputDirectory, workingDirectory, PathComparison))
        {
            throw new ArgumentException("The output directory cannot be the target working directory.", nameof(settings));
        }

        return settings with
        {
            TargetPath = target,
            WorkingDirectory = workingDirectory,
            OutputDirectory = outputDirectory,
            AllowedRoots = allowedRoots,
            ToolDirectory = Path.GetFullPath(settings.ToolDirectory)
        };
    }

    public static bool IsWithin(string path, string root)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        if (string.Equals(fullPath, fullRoot, PathComparison))
        {
            return true;
        }

        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
