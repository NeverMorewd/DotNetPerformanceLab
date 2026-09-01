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
        var resolvedTarget = ResolveLinkTarget(target);
        if (allowedRoots.Length > 0 &&
            (!allowedRoots.Any(root => IsWithin(target, root)) ||
             !allowedRoots.Any(root => IsWithin(resolvedTarget, root)) ||
             !allowedRoots.Any(root => IsWithin(workingDirectory, root))))
        {
            throw new UnauthorizedAccessException("The target executable, its resolved link target, and its working directory must remain inside a configured allowed root.");
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

    private static string ResolveLinkTarget(string target)
    {
        var resolved = new FileInfo(target).ResolveLinkTarget(returnFinalTarget: true);
        return resolved is null ? target : Path.GetFullPath(resolved.FullName);
    }
}
