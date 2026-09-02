(() => {
  "use strict";

  const payload = globalThis.DPL_REPORT;
  const scopeSelect = document.getElementById("scope");
  const metricSelect = document.getElementById("metric");
  const status = document.getElementById("status");
  const metrics = payload.Metrics.filter(item => item.Availability === "Available" && item.Value !== null);
  const scopes = [...new Set(metrics.map(item => item.Scope))];

  for (const scope of scopes) scopeSelect.add(new Option(scope, scope));

  function updateMetrics() {
    const names = [...new Set(metrics.filter(item => item.Scope === scopeSelect.value).map(item => item.Name))].sort();
    metricSelect.replaceChildren(...names.map(name => new Option(name, name)));
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
    const iterations = [...new Set(selected.map(item => item.Iteration))];
    const traces = iterations.map(iteration => {
      const samples = selected.filter(item => item.Iteration === iteration);
      return {
        x: samples.map(item => item.ElapsedSeconds),
        y: samples.map(item => item.Value),
        type: "scatter",
        mode: "lines",
        name: `Iteration ${iteration}`,
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
})();
