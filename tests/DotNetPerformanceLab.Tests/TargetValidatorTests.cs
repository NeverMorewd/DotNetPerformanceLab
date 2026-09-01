namespace DotNetPerformanceLab.Tests;

public sealed class TargetValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dpl-tests-{Guid.NewGuid():N}");

    public TargetValidatorTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void IsWithinAcceptsChildAndRejectsSiblingPrefix()
    {
        var child = Path.Combine(_root, "targets", "app.exe");
        var sibling = Path.Combine(_root, "targets-other", "app.exe");
        var allowed = Path.Combine(_root, "targets");

        Assert.True(TargetValidator.IsWithin(child, allowed));
        Assert.False(TargetValidator.IsWithin(sibling, allowed));
    }

    [Fact]
    public void ValidateRejectsTargetOutsideAllowedRoots()
    {
        var target = Path.Combine(_root, "outside", "app.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, string.Empty);
        var settings = CreateSettings(target, [Path.Combine(_root, "allowed")]);

        Assert.Throws<UnauthorizedAccessException>(() => TargetValidator.Validate(settings));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private RunSettings CreateSettings(string target, IReadOnlyList<string> roots) => new(
        target,
        [],
        Path.GetDirectoryName(target)!,
        Path.Combine(_root, "output"),
        "Test",
        TimeSpan.Zero,
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(1),
        TimeSpan.Zero,
        1,
        false,
        TimeSpan.FromSeconds(5),
        false,
        TimeSpan.FromSeconds(5),
        true,
        roots,
        _root);
}
