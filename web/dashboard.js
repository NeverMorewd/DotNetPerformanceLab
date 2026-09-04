(() => {
  "use strict";

  const bytesPerMebibyte = 1024 * 1024;
  const payload = globalThis.DPL_REPORT;
  const metricSelect = document.getElementById("metric");
  const scopesContainer = document.getElementById("scopes");
  const status = document.getElementById("status");
  const runtimeNote = document.getElementById("runtime-note");
  let metrics = payload.Metrics.filter(item => item.Availability === "Available" && item.Value !== null);
  let selectedScope = preferredScope(metrics);

  const displayNames = {
    "process.cpu.core_equivalent": "CPU — core equivalent",
    "process.cpu.machine_normalized": "CPU — machine normalized",
    "process.cpu.user": "CPU — user time",
    "process.cpu.system": "CPU — system time",
    "process.memory.working_set": "Memory — working set",
    "process.memory.private": "Memory — private",
    "process.memory.virtual": "Memory — virtual",
    "process.thread.count": "Threads",
    "process.handle.count": "Handles",
    "process.io.read.operations": "I/O — read operations",
    "process.io.write.operations": "I/O — write operations",
    "process.io.read.bytes": "I/O — bytes read",
    "process.io.write.bytes": "I/O — bytes written",
    "host.cpu.usage": "Host CPU usage",
    "host.memory.total": "Host memory — total",
    "host.memory.available": "Host memory — available",
    "host.memory.used": "Host memory — used",
    "host.swap.total": "Host swap — total",
    "host.swap.used": "Host swap — used",
    "host.network.receive": "Host network — received",
    "host.network.transmit": "Host network — transmitted",
    "host.process.count": "Host process count",
    "host.load.1m": "Host load average — 1 minute",
    "host.load.5m": "Host load average — 5 minutes",
    "host.load.15m": "Host load average — 15 minutes"
  };

  function preferredScope(items) {
    const available = new Set(items.map(item => item.Scope));
    return ["Process", "Runtime", "Host", "Application"].find(scope => available.has(scope)) ?? "";
  }

  function orderedScopes() {
    const available = new Set(metrics.map(item => item.Scope));
    return ["Process", "Runtime", "Host", "Application"].filter(scope => available.has(scope));
  }

  function metricName(name) {
    if (displayNames[name]) return displayNames[name];
    return name
      .replace(/^dotnet\./, "")
      .replaceAll(".", " ")
      .replaceAll("_", " ")
      .replace(/\b\w/g, character => character.toUpperCase())
      .replace(/\bGc\b/g, "GC")
      .replace(/\bJit\b/g, "JIT")
      .replace(/\bCpu\b/g, "CPU")
      .replace(/\bIl\b/g, "IL")
      .replace(/\bIo\b/g, "I/O");
  }

  function renderScopes() {
    scopesContainer.replaceChildren(...orderedScopes().map(scope => {
      const count = new Set(metrics.filter(item => item.Scope === scope).map(item => item.Name)).size;
      const button = document.createElement("button");
      button.type = "button";
      button.className = "scope-tab";
      button.dataset.scope = scope;
      button.setAttribute("aria-pressed", String(scope === selectedScope));
      button.innerHTML = `<span>${scope}</span><span class="scope-count">${count}</span>`;
      button.addEventListener("click", () => {
        selectedScope = scope;
        renderScopes();
        updateMetrics();
      });
      return button;
    }));
  }

  function updateMetrics() {
    const previous = metricSelect.value;
    const names = [...new Set(metrics.filter(item => item.Scope === selectedScope).map(item => item.Name))]
      .sort((left, right) => metricName(left).localeCompare(metricName(right)));
    metricSelect.replaceChildren(...names.map(name => new Option(metricName(name), name)));
    if (names.includes(previous)) metricSelect.value = previous;
    runtimeNote.hidden = selectedScope !== "Runtime";
    render();
  }

  function presentation(unit, values) {
    if (unit === "By") return { unit: "MiB", values: values.map(value => value / bytesPerMebibyte), decimals: 2 };
    if (unit === "By / 1 sec") return { unit: "MiB/s", values: values.map(value => value / bytesPerMebibyte), decimals: 3 };
    if (unit === "s / 1 sec") return { unit: "ms/s", values: values.map(value => value * 1000), decimals: 3 };
    const normalized = unit.startsWith("{") && unit.endsWith("}") ? unit.slice(1, -1) : unit;
    return { unit: normalized, values, decimals: normalized === "%" ? 2 : 3 };
  }

  function render() {
    const selected = metrics.filter(item => item.Scope === selectedScope && item.Name === metricSelect.value);
    if (!selected.length) {
      status.textContent = "No samples available";
      Plotly.purge("chart");
      return;
    }

    const series = new Map();
    for (const sample of selected) {
      const tags = Object.entries(sample.Tags ?? {}).sort(([left], [right]) => left.localeCompare(right));
      const tagLabel = tags.map(([key, value]) => `${key}=${value}`).join(", ");
      const key = `${sample.Iteration}|${tagLabel}`;
      if (!series.has(key)) series.set(key, { iteration: sample.Iteration, tagLabel, samples: [] });
      series.get(key).samples.push(sample);
    }

    const rawUnit = selected[0].Unit ?? "";
    const formatted = presentation(rawUnit, selected.map(item => item.Value));
    document.getElementById("scope-label").textContent = `${selectedScope} metric`;
    document.getElementById("metric-title").textContent = metricName(metricSelect.value);
    document.getElementById("metric-code").textContent = metricSelect.value;
    status.textContent = `${selected.length.toLocaleString()} samples${formatted.unit ? ` · ${formatted.unit}` : ""}`;

    const traces = [...series.values()].map(seriesItem => {
      const samples = seriesItem.samples.sort((left, right) => left.ElapsedSeconds - right.ElapsedSeconds);
      const values = presentation(rawUnit, samples.map(item => item.Value));
      const iterationLabel = seriesItem.iteration > 0 ? `Iteration ${seriesItem.iteration}` : "Diagnostic pass";
      return {
        x: samples.map(item => item.ElapsedSeconds),
        y: values.values,
        type: "scatter",
        mode: "lines",
        name: seriesItem.tagLabel ? `${iterationLabel} · ${seriesItem.tagLabel}` : iterationLabel,
        line: { width: 1.8 },
        hovertemplate: `%{x:.1f}s<br>%{y:.${values.decimals}f} ${values.unit}<extra>%{fullData.name}</extra>`
      };
    });

    Plotly.react("chart", traces, {
      paper_bgcolor: "rgba(0,0,0,0)",
      plot_bgcolor: "rgba(0,0,0,0)",
      font: { color: "#dce8ff", family: "Inter, system-ui, sans-serif", size: 12 },
      colorway: ["#56b4ff", "#9b8cff", "#43d6a2", "#ffb85c", "#ff7597", "#74d7ec"],
      hovermode: "x unified",
      margin: { l: 72, r: 28, t: 24, b: traces.length > 1 ? 112 : 68 },
      xaxis: {
        title: { text: "Elapsed time (seconds)", standoff: 14, font: { size: 12 } },
        gridcolor: "#23324d",
        zerolinecolor: "#334564",
        automargin: true,
        fixedrange: false
      },
      yaxis: {
        title: { text: formatted.unit, standoff: 10, font: { size: 12 } },
        gridcolor: "#23324d",
        zerolinecolor: "#334564",
        automargin: true,
        rangemode: "tozero"
      },
      legend: {
        orientation: "h",
        x: 0,
        xanchor: "left",
        y: -0.2,
        yanchor: "top",
        font: { size: 11 }
      }
    }, {
      responsive: true,
      displaylogo: false,
      scrollZoom: false,
      modeBarButtonsToRemove: ["toImage", "sendDataToCloud", "lasso2d", "select2d"]
    });
  }

  document.getElementById("target").textContent = payload.Target;
  document.getElementById("generated").textContent = new Date(payload.GeneratedUtc).toLocaleString();
  document.getElementById("environment").textContent = `${payload.Environment.OperatingSystem} · ${payload.Environment.ProcessArchitecture}`;
  document.getElementById("baseline").textContent = `${payload.Settings.Iterations} × ${payload.Settings.MeasurementSeconds}s`;
  document.getElementById("diagnostics").textContent = payload.Counters.Collected ? "Runtime counters available" : "Runtime counters unavailable";
  metricSelect.addEventListener("change", render);
  renderScopes();
  updateMetrics();

  const parameters = new URLSearchParams(location.search);
  const liveEndpoint = parameters.get("live");
  const liveRunId = parameters.get("run");
  if (liveEndpoint && liveRunId) startLiveUpdates(liveEndpoint, liveRunId);

  async function startLiveUpdates(endpoint, runId) {
    let base;
    try {
      base = new URL(endpoint);
      if (base.protocol !== "https:" && base.hostname !== "localhost") throw new Error("Live endpoints must use HTTPS.");
    } catch (error) {
      status.textContent = `Live connection rejected: ${error.message}`;
      return;
    }

    let sequence = 0;
    while (true) {
      try {
        const url = new URL(`runs/${encodeURIComponent(runId)}/metrics`, base.toString().replace(/\/?$/, "/"));
        url.searchParams.set("after", sequence.toString());
        const response = await fetch(url, { headers: { Accept: "application/json" }, cache: "no-store" });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const batch = await response.json();
        sequence = Math.max(sequence, batch.Sequence ?? 0);
        const incoming = (batch.Metrics ?? []).filter(item => item.Availability === "Available" && item.Value !== null);
        if (incoming.length) {
          metrics = metrics.concat(incoming);
          if (!orderedScopes().includes(selectedScope)) selectedScope = preferredScope(metrics);
          renderScopes();
          updateMetrics();
        }
        if (batch.Completed) return;
      } catch (error) {
        status.textContent = `Live connection unavailable: ${error.message}`;
      }
      await new Promise(resolve => setTimeout(resolve, 2000));
    }
  }
})();
