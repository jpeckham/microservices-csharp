# Health Check Endpoints

## What

Add a `GET /health` endpoint to each of the five microservice APIs (Identity, Post, Social, Engagement, Feed). The endpoint returns HTTP 200 with a `Healthy` status when the service is running. This enables Docker/Kubernetes liveness probes, load-balancer checks, and operational dashboards.

This mirrors the `GET /health` endpoint in the clean-architecture-csharp reference implementation.

## Implementation

ASP.NET Core has built-in health check support via `Microsoft.Extensions.Diagnostics.HealthChecks` (included in the framework — no extra NuGet packages needed):

```csharp
builder.Services.AddHealthChecks();   // registration
app.MapHealthChecks("/health");       // mapping (before app.Run())
```

The default response is `text/plain` with body `Healthy`, `Degraded`, or `Unhealthy`. No custom checks are wired in this iteration — a single liveness probe is sufficient.

## Affected Services

| Service | File |
|---------|------|
| Identity.Api | `src/Identity.Api/Program.cs` |
| Post.Api | `src/Post.Api/Program.cs` |
| Social.Api | `src/Social.Api/Program.cs` |
| Engagement.Api | `src/Engagement.Api/Program.cs` |
| Feed.Api | `src/Feed.Api/Program.cs` |

## Tests

One integration test per API: `GET /health` returns `200 OK`. Tests live alongside existing API test classes.
