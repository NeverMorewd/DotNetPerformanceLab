# .NET Performance Lab

Reusable GitHub Actions workflows and a cross-platform performance harness for measuring .NET applications on self-hosted runners.

The lab generates an offline interactive Plotly report, Markdown summaries, normalized JSON and CSV samples, runtime counters, and optional EventPipe traces. Baseline process and host sampling is isolated from diagnostic collection so profiler overhead does not contaminate the primary CPU and memory measurements.

## Capabilities

- Windows, macOS, and Linux process sampling through .NET APIs.
- Caller-built .NET 8+ framework-dependent, self-contained, trimmed, and Native AOT applications.
- Baseline process and host sampling for local legacy or non-.NET executables, with managed diagnostics reported as unavailable when unsupported.
- CPU core-equivalent and machine-normalized usage.
- Working set, private memory, thread count, percentiles, variation, and growth rate.
- Synchronized host CPU, physical memory, swap, network, process-count, and load-average measurements where supported.
- A versioned normalized metric model with explicit availability and collector capabilities.
- Pinned `dotnet-counters` and `dotnet-trace` diagnostic passes.
- Runtime summaries for GC, allocation, ThreadPool, contention, assemblies, and JIT metrics.
- Optional application and framework meters from `System.Diagnostics.Metrics`, including ASP.NET Core, System.Net, and application-defined instruments.
- Markdown, JSON, CSV, `.nettrace`, and GitHub job-summary output.
- Offline Plotly.js charts with human-readable units, hover details, zooming, and explicit process, runtime, host, and application scopes.
- A reusable GitHub Pages history site assembled from unexpired report artifacts.
- Independent iterations with deterministic target-process cleanup.

## Security model

Performance workflows execute applications on a self-hosted machine. The reusable profiling workflows therefore:

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

## Build and analyze a repository application

Source selection and compilation belong to the caller repository. The caller checks out any branch, tag, or commit, performs its own restore and publish process, and uploads the complete application as a workflow artifact. DotNetPerformanceLab downloads that artifact in a separate profiling job and is responsible only for validation, execution, collection, and reporting.

This boundary supports .NET 8 and later without coupling the profiler to a repository layout, SDK version, build system, signing process, workload, or publish properties. It also prevents arbitrary build commands from being passed into a reusable workflow as strings.

```yaml
name: Analyze repository performance

on:
  workflow_dispatch:

permissions:
  contents: read

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: owner/application
          ref: main
          persist-credentials: false
      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 8.0.x
      - run: dotnet publish src/MyApplication.csproj --configuration Release --runtime win-x64 --self-contained true --output '${{ runner.temp }}/performance-target'
      - uses: actions/upload-artifact@v7
        with:
          name: performance-target-Windows
          path: ${{ runner.temp }}/performance-target
          retention-days: 1

  performance:
    needs: build
    uses: NeverMorewd/DotNetPerformanceLab/.github/workflows/profile-artifact.yml@v3
    with:
      platform: Windows
      artifact-name: performance-target-Windows
      executable-path: MyApplication.exe
      report-label: My Application
      meters-json: '["Microsoft.AspNetCore.Hosting","System.Net.Http"]'
```

The complete configurable example is [`templates/dotnet-build-and-profile.yml`](templates/dotnet-build-and-profile.yml). Copy it into the caller repository and customize its build job. Public targets need no additional credentials. A private target needs a fine-grained token with read-only Contents access, used only by the caller's checkout step.

`profile-repository.yml` remains available as a v2 compatibility workflow while existing callers migrate. New integrations should use `profile-artifact.yml`; the coupled repository workflow is scheduled for removal in the next major version.

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
├── comparison-data.json
├── samples-iteration-1.csv
├── runtime-counters.json
├── runtime.nettrace
├── web-report/
│   ├── index.html
│   ├── comparison-data.json
│   └── assets/
│       ├── plotly-basic.min.js
│       ├── dashboard.js
│       ├── dashboard.css
│       └── data.js
```

Run `npm ci --prefix web --ignore-scripts` before invoking the harness directly. Reusable workflows restore the pinned Plotly dependency automatically. The generated `web-report/index.html` works from an extracted artifact without a web server or network connection.

Unavailable EventPipe diagnostics do not invalidate baseline results. This allows the external workflow to measure .NET Framework and non-.NET processes while clearly reporting that managed diagnostics were unavailable.

## Compare multiple reports

Every profile exports a versioned `comparison-data.json` contract containing minimum, mean, maximum, median, P95, final value, standard deviation, coefficient of variation, growth rate, sample count, unit, tags, and optimization direction for every available metric series. The same file is published beside the interactive report on GitHub Pages; use the **Data** link in the history table to copy its URL.

Call the reusable comparison workflow with two to twelve report URLs:

```yaml
jobs:
  compare:
    uses: NeverMorewd/DotNetPerformanceLab/.github/workflows/compare-reports.yml@v2
    with:
      title: Before and after optimization
      reports-json: >-
        [
          {"label":"Before","url":"https://owner.github.io/dashboard/runs/100/200/comparison-data.json"},
          {"label":"After","url":"https://owner.github.io/dashboard/runs/101/201/comparison-data.json"}
        ]
      interactive-report-url: https://owner.github.io/dashboard/
```

The result contains one comprehensive table with minimum, mean, maximum, P95, and final values for every compatible metric. CPU, memory, allocation, pause, exception, contention, and queue metrics are ranked lower-is-better. Explicit throughput, completion, and success metrics are ranked higher-is-better. Host context and metrics with ambiguous semantics remain neutral and are compared without a winner. Red markers identify best values and include a symbol so the result does not depend on color alone.

Only compare reports produced under the same workload, operating system, architecture, runner, power policy, duration, and sampling policy. The tool shows heterogeneous reports but does not claim that they form a valid benchmark.

Diagnostic tools receive a bounded 60-second completion grace beyond the requested collection duration. This accommodates slower EventPipe attachment and output finalization for Native AOT desktop processes without allowing a stalled diagnostic pass to hold a runner indefinitely.

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
      interactive-report-url: https://owner.github.io/performance-dashboard/

  pages:
    needs: performance
    uses: NeverMorewd/DotNetPerformanceLab/.github/workflows/deploy-pages.yml@v2
    with:
      history-days: 30
      maximum-runs: 50
```

Set `interactive-report-url` to the deployed Pages root of the caller repository. The profiling job summary then links directly to the online report instead of asking readers to download chart files. Leave it unset when the caller does not publish a Pages dashboard.

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

The repository profiling workflow enables this property automatically when it publishes a Native AOT target and runtime counters or traces are requested. External executable profiling cannot modify an existing binary; its publisher must enable EventPipe before compilation.

Native AOT exposes only a subset of runtime events and does not support standard managed heap analysis. Baseline process metrics remain available regardless.

## Versioning

Production callers should use the moving major tag `v2` or pin a full commit SHA for maximum supply-chain stability. The `main` branch is for development and is not a stable interface.

## License

Licensed under the MIT License.
