# .NET Performance Lab

Reusable GitHub Actions workflows and a cross-platform performance harness for measuring .NET applications on self-hosted runners.

The lab generates an offline interactive Plotly report, Markdown summaries, normalized JSON and CSV samples, SVG charts, runtime counters, and optional EventPipe traces. Baseline process and host sampling is isolated from diagnostic collection so profiler overhead does not contaminate the primary CPU and memory measurements.

## Capabilities

- Windows, macOS, and Linux process sampling through .NET APIs.
- Framework-dependent, self-contained, Native AOT, .NET Framework, and non-.NET targets.
- CPU core-equivalent and machine-normalized usage.
- Working set, private memory, thread count, percentiles, variation, and growth rate.
- Synchronized host CPU, physical memory, swap, network, process-count, and load-average measurements where supported.
- A versioned normalized metric model with explicit availability and collector capabilities.
- Pinned `dotnet-counters` and `dotnet-trace` diagnostic passes.
- Runtime summaries for GC, allocation, ThreadPool, contention, assemblies, and JIT metrics.
- Optional application and framework meters from `System.Diagnostics.Metrics`, including ASP.NET Core, System.Net, and application-defined instruments.
- Markdown, JSON, CSV, SVG, `.nettrace`, and GitHub job-summary output.
- Offline Plotly.js charts with hover details, zooming, iteration selection, and SVG export.
- A reusable GitHub Pages history site assembled from unexpired report artifacts.
- Independent iterations with deterministic target-process cleanup.

## Security model

Performance workflows execute applications on a self-hosted machine. Both reusable workflows therefore:

- run only when the caller was started with `workflow_dispatch`;
- require the fixed `self-hosted` and `metric-test` runner labels;
- use the fixed `performance-lab` GitHub Environment;
- request read-only repository permissions;
- pass paths and arguments as data to `ProcessStartInfo` without shell interpolation.

Configure required reviewers on the caller repository's `performance-lab` Environment. Use a dedicated, non-administrator runner account and never route pull-request workflows to a personal workstation.

The external workflow additionally requires an allowed root. The executable must resolve inside that directory.

## Runner labels

Every performance runner needs the custom `metric-test` label and an operating-system label:

```text
self-hosted, metric-test, Windows
self-hosted, metric-test, Linux
self-hosted, metric-test, macOS
```

Desktop applications need a signed-in interactive desktop session. Windows Session 0, headless macOS agents, and Linux runners without a desktop session are not representative UI test environments.

## Analyze a repository application

The caller workflow must be manually dispatched. It publishes the requested project and analyzes the resulting executable on the selected runner.

```yaml
name: Analyze repository performance

on:
  workflow_dispatch:

permissions:
  contents: read

jobs:
  performance:
    uses: NeverMorewd/DotNetPerformanceLab/.github/workflows/profile-repository.yml@v2
    with:
      platform: Windows
      project-path: src/MyApplication.csproj
      runtime-identifier: win-x64
      executable-path: MyApplication.exe
      report-label: My Application
      self-contained: true
      publish-aot: false
      meters-json: '["Microsoft.AspNetCore.Hosting","System.Net.Http"]'
```

The profiling workflow can live in a separate dashboard repository. Set `target-repository` and `target-ref`; reports and artifacts then belong to the caller while source is checked out from the target:

```yaml
jobs:
  performance:
    uses: NeverMorewd/DotNetPerformanceLab/.github/workflows/profile-repository.yml@v2
    with:
      target-repository: owner/application
      target-ref: main
      platform: Windows
      project-path: src/Application.csproj
      runtime-identifier: win-x64
      executable-path: Application.exe
```

Public targets need no additional credentials. For private targets, pass a fine-grained read-only token through the reusable workflow's `target-repository-token` secret. The token is used only by checkout and credentials are not persisted.

Use `MyApplication` rather than `MyApplication.exe` for published Linux and macOS app hosts.

## Analyze an external executable

The target must already exist on the selected self-hosted runner. Keep external targets in a dedicated directory.

```yaml
name: Analyze external performance

on:
  workflow_dispatch:
    inputs:
      executable-path:
        description: Absolute executable path below C:\PerformanceTargets
        required: true
        type: string

permissions:
  contents: read

jobs:
  performance:
    uses: NeverMorewd/DotNetPerformanceLab/.github/workflows/profile-external.yml@v2
    with:
      platform: Windows
      executable-path: ${{ inputs.executable-path }}
      allowed-root: C:\PerformanceTargets
      report-label: External application
```

Arguments use a JSON array and are never interpreted by a command shell:

```yaml
arguments-json: '["--profile","Performance Test","--duration","300"]'
```

## Recommended measurement settings

| Setting | Default |
|---|---:|
| Warm-up | 60 seconds |
| Measurement | 300 seconds |
| Sample interval | 1 second |
| Cooldown | 10 seconds |
| Iterations | 3 |
| Runtime counters | Enabled, separate 30-second pass |
| EventPipe trace | Disabled, optional separate 30-second pass |

Use the same physical runner, power profile, workload, application state, and settings when comparing versions. Cross-machine or cross-operating-system numbers are useful independently but should not be treated as direct regressions.

## Artifacts

Each run uploads:

```text
dotnet-performance-report/
├── report.md
├── job-summary.md
├── summary.json
├── metrics.json
├── samples-iteration-1.csv
├── runtime-counters.json
├── runtime.nettrace
├── web-report/
│   ├── index.html
│   └── assets/
│       ├── plotly-basic.min.js
│       ├── dashboard.js
│       ├── dashboard.css
│       └── data.js
└── charts/
    ├── cpu.svg
    ├── working-set.svg
    ├── private-memory.svg
    ├── host-cpu.svg
    └── host-memory.svg
```

Run `npm ci --prefix web --ignore-scripts` before invoking the harness directly. Reusable workflows restore the pinned Plotly dependency automatically. The generated `web-report/index.html` works from an extracted artifact without a web server or network connection.

Unavailable EventPipe diagnostics do not invalidate baseline results. This allows the external workflow to measure .NET Framework and non-.NET processes while clearly reporting that managed diagnostics were unavailable.

## GitHub Pages history

Enable GitHub Pages with **GitHub Actions** as its source in the caller repository, then add a deployment job after the profiling job. The site generator reads report artifacts through the GitHub API, publishes only artifacts that have not expired, and rebuilds the site from scratch on each deployment. Artifact retention therefore remains the single source of truth for report retention.

```yaml
permissions:
  contents: read
  actions: read
  pages: write
  id-token: write

jobs:
  performance:
    uses: NeverMorewd/DotNetPerformanceLab/.github/workflows/profile-repository.yml@v2
    with:
      platform: Windows
      project-path: src/MyApplication.csproj
      runtime-identifier: win-x64
      executable-path: MyApplication.exe
      report-label: My Application

  pages:
    needs: performance
    uses: NeverMorewd/DotNetPerformanceLab/.github/workflows/deploy-pages.yml@v2
    with:
      history-days: 30
      maximum-runs: 50
```

Add a separate manually dispatched or scheduled caller when expired reports should disappear even when no new profile is produced:

```yaml
name: Refresh performance history

on:
  workflow_dispatch:
  schedule:
    - cron: '17 3 * * *'

permissions:
  contents: read
  actions: read
  pages: write
  id-token: write

jobs:
  pages:
    uses: NeverMorewd/DotNetPerformanceLab/.github/workflows/deploy-pages.yml@v2
```

GitHub Pages is a static host and is not a live telemetry transport. The offline and history reports are complete in this stage; live viewing will use the same normalized metric schema through an explicit optional publisher contract and a separately authenticated ingestion endpoint.

For live viewing, pass `live-endpoint` to a profiling workflow and map the `live-token` secret. The runner publishes through a bounded background queue; it never embeds the secret in the report. Open the Pages report with `?live=<encoded-api-base-url>&run=<github-run-id>-<run-attempt>`. The API contract and security requirements are documented in [Live telemetry protocol](docs/live-telemetry-protocol.md).

## Native AOT

Native AOT requires EventPipe to use `dotnet-counters` and `dotnet-trace`:

```xml
<EventSourceSupport>true</EventSourceSupport>
```

Native AOT exposes only a subset of runtime events and does not support standard managed heap analysis. Baseline process metrics remain available regardless.

## Versioning

Production callers should use the moving major tag `v2` or pin a full commit SHA for maximum supply-chain stability. The `main` branch is for development and is not a stable interface.

## License

Licensed under the MIT License.
