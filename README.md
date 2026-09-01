# .NET Performance Lab

Reusable GitHub Actions workflows and a cross-platform performance harness for measuring .NET applications on self-hosted runners.

The project produces a portable Markdown report together with raw JSON, CSV, charts, and optional EventPipe diagnostics. Baseline process sampling is kept separate from diagnostic collection so profiler overhead does not contaminate the primary CPU and memory measurements.

## Goals

- Measure any executable with cross-platform process APIs.
- Add managed runtime metrics when EventPipe is available.
- Support framework-dependent, self-contained, and Native AOT applications.
- Generate deterministic, downloadable reports for Windows, macOS, and Linux.
- Keep caller workflows small by exposing versioned reusable workflows.
- Degrade gracefully for .NET Framework and non-.NET processes.

## Planned reusable workflows

### Repository application

Builds an application from the caller repository and analyzes the published executable.

```yaml
jobs:
  performance:
    uses: NeverMorewd/DotNetPerformanceLab/.github/workflows/profile-repository.yml@v1
    with:
      project-path: src/MyApplication.csproj
      runtime-identifier: win-x64
      executable-path: publish/MyApplication.exe
```

### External executable

Analyzes an executable that already exists on the selected self-hosted runner.

```yaml
jobs:
  performance:
    uses: NeverMorewd/DotNetPerformanceLab/.github/workflows/profile-external.yml@v1
    with:
      runner-labels: '["self-hosted","Windows","X64","metric-test"]'
      executable-path: C:\PerformanceTargets\MyApplication\MyApplication.exe
```

External executable analysis is intentionally restricted to manually dispatched caller workflows. Inputs are passed to the harness as data and are never interpolated into a shell command.

## Measurement model

Each iteration has isolated warm-up and measurement phases. The baseline pass samples the target process tree without attaching a managed profiler. Optional diagnostic passes collect runtime counters and traces separately.

Primary report statistics include median, 95th percentile, maximum, final value, standard deviation, coefficient of variation, and memory growth rate. Reports include enough environment and toolchain metadata to determine whether two runs are meaningfully comparable.

## Runner requirements

Desktop applications require a signed-in interactive self-hosted runner on the target operating system. A Windows service running in Session 0 is not suitable for UI application analysis. Equivalent self-hosted runners are required for macOS and Linux desktop measurements.

## Security

Self-hosted runners execute repository-controlled code on the host machine. Use dedicated non-administrator runner accounts, restrict workflow permissions, require manual dispatch or protected environments for external targets, and do not expose a personal workstation to untrusted pull requests.

## License

Licensed under the MIT License.
