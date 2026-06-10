# Feature: Handle CommentDeleted in Feed.Api

## Source
The reference decrements engagement counts symmetrically — every add event has a matching remove event.
In `Feed.Api`, `CommentAdded` increments `CommentCount` on feed entries, but `CommentDeleted` has
no handler. Deleting a comment leaves the feed count permanently inflated.

## What to add
Add a `CommentDeleted` event handler in both the local HTTP sink and the Azure Service Bus consumer
inside `Feed.Api/Program.cs`. The handler decrements `CommentCount` by 1, floored at 0, matching
the guard already in place on the `LikeRemoved` handler.

## Affected Files

| File | Change |
|------|--------|
| `src/Feed.Api/Program.cs` | Add `POST /events/CommentDeleted` endpoint and `CommentDeleted` case in `ServiceBusFeedConsumer` |
| `tests/Integration.Tests/FeedApiTests.cs` | New tests: comment count decrements on delete, does not go below zero |

## Implementation

```csharp
// local event sink
app.MapPost("/events/CommentDeleted", async (CommentDeleted integrationEvent, IMongoCollection<FeedEntryDocument> entries, CancellationToken ct) =>
{
    await entries.UpdateOneAsync(
        e => e.PostId == integrationEvent.PostId && e.CommentCount > 0,
        Builders<FeedEntryDocument>.Update.Inc(e => e.CommentCount, -1),
        cancellationToken: ct);
    return Results.Accepted();
});
```

Mirror the same logic in the `ServiceBusFeedConsumer` switch block.
