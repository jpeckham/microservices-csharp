# AppSession Concurrent Initialization Fix

## Source

The reference's service-layer patterns rely on single-initialisation semantics — a
resource is fetched once and the result is reused. Blazor WASM components commonly
call `InitAsync()` from multiple component lifecycle hooks that run concurrently
(e.g. `MainLayout.OnInitializedAsync` and `Home.OnInitializedAsync`). Without
proper synchronisation the session can be populated twice or, worse, components can
observe a "not logged in" state while the HTTP call is still in flight.

## Bug (now fixed)

An earlier implementation used a `bool _initialized` flag:

```csharp
// BUGGY — _initialized set BEFORE await, so second caller sees true too soon
public async Task InitAsync()
{
    if (_initialized) return;
    _initialized = true;          // ← set before HTTP call finishes
    await FetchUserAsync();
}
```

When two components called `InitAsync()` simultaneously the second returned
immediately (before the HTTP call resolved) and rendered with `IsLoggedIn == false`.

## Fix

Replace the flag with a `Task?` field and use the null-coalescing assignment
(`??=`) so all concurrent callers share the same pending `Task`:

```csharp
public Task InitAsync() => _initTask ??= FetchUserAsync();
```

`_initTask ??= FetchUserAsync()` is thread-safe for the read path in Blazor WASM
(single-threaded JS runtime) and ensures every caller awaits the same Task object.

## Tests

Two unit tests in `AppSessionTests.cs`:

1. `InitAsync_WhenCalledConcurrently_BothCallersWaitForSessionToBePopulated` —
   uses a `SemaphoreSlim` gate to hold the HTTP response; asserts the second
   caller's task is still pending before the gate is released.
2. `InitAsync_SubsequentCall_ReturnsSameCompletedTask` — after the first
   `InitAsync()` completes, a second call must return an already-completed Task
   without triggering a second HTTP round-trip.

## Affected Files

| File | Change |
|------|--------|
| `src/Social.Web.Client/Services/AppSession.cs` | `_initTask ??= FetchUserAsync()` pattern |
| `tests/Integration.Tests/AppSessionTests.cs` | New unit tests (no Testcontainer needed) |
| `tests/Integration.Tests/Integration.Tests.csproj` | Added `Social.Web.Client` project reference |
