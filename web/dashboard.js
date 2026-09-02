(() => {
  "use strict";

  const payload = globalThis.DPL_REPORT;
  const scopeSelect = document.getElementById("scope");
  const metricSelect = document.getElementById("metric");
  const status = document.getElementById("status");
  let metrics = payload.Metrics.filter(item => item.Availability === "Available" && item.Value !== null);
  let scopes = [...new Set(metrics.map(item => item.Scope))];

  for (const scope of scopes) scopeSelect.add(new Option(scope, scope));

  function updateMetrics() {
    const selectedMetric = metricSelect.value;
    const names = [...new Set(metrics.filter(item => item.Scope === scopeSelect.value).map(item => item.Name))].sort();
    metricSelect.replaceChildren(...names.map(name => new Option(name, name)));
    if (names.includes(selectedMetric)) metricSelect.value = selectedMetric;
    render();
  }

  function render() {
    const selected = metrics.filter(item => item.Scope === scopeSelect.value && item.Name === metricSelect.value);
    if (!selected.length) {
      status.textContent = "No samples are available for this metric.";
      Plotly.purge("chart");
      return;
    }

    status.textContent = `${selected.length} samples · ${selected[0].Unit}`;
    const series = new Map();
    for (const sample of selected) {
      const tags = Object.entries(sample.Tags ?? {}).sort(([left], [right]) => left.localeCompare(right));
      const tagLabel = tags.map(([key, value]) => `${key}=${value}`).join(", ");
      const key = `${sample.Iteration}|${tagLabel}`;
      if (!series.has(key)) series.set(key, { iteration: sample.Iteration, tagLabel, samples: [] });
      series.get(key).samples.push(sample);
    }

    const traces = [...series.values()].map(seriesItem => {
      const samples = seriesItem.samples.sort((left, right) => left.ElapsedSeconds - right.ElapsedSeconds);
      const iterationLabel = seriesItem.iteration > 0 ? `Iteration ${seriesItem.iteration}` : "Diagnostic pass";
      return {
        x: samples.map(item => item.ElapsedSeconds),
        y: samples.map(item => item.Value),
        type: "scatter",
        mode: "lines",
        name: seriesItem.tagLabel ? `${iterationLabel} · ${seriesItem.tagLabel}` : iterationLabel,
        line: { width: 2 },
        hovertemplate: "%{x:.2f}s<br>%{y:.3f}<extra>%{fullData.name}</extra>"
      };
    });

    Plotly.react("chart", traces, {
      title: { text: metricSelect.value, x: .02, font: { size: 18 } },
      paper_bgcolor: "rgba(0,0,0,0)",
      plot_bgcolor: "rgba(0,0,0,0)",
      font: { color: "#dce8ff" },
      hovermode: "x unified",
      margin: { l: 72, r: 30, t: 58, b: 62 },
      xaxis: { title: "Elapsed time (seconds)", gridcolor: "#23324d", zerolinecolor: "#334564" },
      yaxis: { title: selected[0].Unit, gridcolor: "#23324d", zerolinecolor: "#334564" },
      legend: { orientation: "h", y: 1.12 }
    }, {
      responsive: true,
      displaylogo: false,
      toImageButtonOptions: { format: "svg", filename: `${scopeSelect.value}-${metricSelect.value}` }
    });
  }

  document.getElementById("target").textContent = payload.Target;
  document.getElementById("generated").textContent = new Date(payload.GeneratedUtc).toLocaleString();
  document.getElementById("environment").textContent = `${payload.Environment.OperatingSystem} · ${payload.Environment.ProcessArchitecture}`;
  scopeSelect.addEventListener("change", updateMetrics);
  metricSelect.addEventListener("change", render);
  scopeSelect.value = scopes.includes("Process") ? "Process" : scopes[0];
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
          scopes = [...new Set(metrics.map(item => item.Scope))];
          const selectedScope = scopeSelect.value;
          scopeSelect.replaceChildren(...scopes.map(scope => new Option(scope, scope)));
          scopeSelect.value = scopes.includes(selectedScope) ? selectedScope : scopes[0];
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
