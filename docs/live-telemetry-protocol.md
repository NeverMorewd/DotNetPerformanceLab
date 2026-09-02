# Live telemetry protocol

GitHub Pages is the read-only dashboard host. A separately deployed HTTPS API is required to ingest samples from a runner and expose them to browsers.

## Runner to ingestion API

The harness sends authenticated `POST` requests to:

```text
{base-url}/runs/{run-id}/metrics
```

The request uses `Authorization: Bearer {token}` and a JSON `LiveMetricBatch` body. Schema version 1 contains `RunId`, monotonic `Sequence`, `SentUtc`, `Metrics`, and `Completed`. Metric values use the same `MetricSample` contract as `metrics.json`.

Publishing is optional and uses a bounded background queue so network latency never blocks the sampling loop. A failed live endpoint does not invalidate the baseline report. The final batch has `Completed: true` and an empty metric array.

## Dashboard to query API

The API must expose an unauthenticated or independently browser-authorized endpoint with CORS enabled:

```text
GET {base-url}/runs/{run-id}/metrics?after={sequence}
```

It returns the next `LiveMetricBatch`. The dashboard polls every two seconds and stops after `Completed` is true. Open a deployed report with these query parameters:

```text
?live=https%3A%2F%2Fmetrics.example.com%2Fapi%2F&run=123456789-1
```

The ingestion bearer token must never be embedded in GitHub Pages, workflow artifacts, URLs, or JavaScript. The API should enforce request-size limits, token rotation, per-run expiration, CORS allowlists, and a maximum sample count. A future reference backend can implement this protocol without coupling the core harness or static reports to a cloud vendor.
