using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Shared.Contracts.Auth;
using Shared.Contracts.Events;
using Shared.Contracts.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IEventPublisher, ConfiguredEventPublisher>();
builder.Services.AddSingleton(_ =>
{
    var connectionString = builder.Configuration["Mongo:ConnectionString"] ?? "mongodb://localhost:27017";
    var databaseName = builder.Configuration["Mongo:DatabaseName"] ?? "PostDb";
    return new MongoClient(connectionString).GetDatabase(databaseName);
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<PostDocument>("posts"));

var jwtKey = builder.Configuration["Jwt:Key"] ?? "local-development-secret-change-me-32";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/posts", async (
    CreatePostRequest request,
    ClaimsPrincipal principal,
    IMongoCollection<PostDocument> posts,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Content)) return Results.BadRequest(new { error = "Post content is required." });

    var post = new PostDocument
    {
        Id = Guid.NewGuid(),
        AuthorId = userId.Value,
        AuthorHandle = principal.FindFirstValue(AuthConstants.HandleClaim) ?? $"@{userId.Value.ToString()[..8]}",
        AuthorDisplayName = principal.FindFirstValue(AuthConstants.DisplayNameClaim) ?? "Unknown user",
        Content = request.Content.Trim(),
        PostedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    await posts.InsertOneAsync(post, cancellationToken: ct);
    await events.PublishAsync(new PostCreated(post.Id, post.AuthorId, post.AuthorHandle, post.AuthorDisplayName, post.Content, DateTimeOffset.UtcNow), ct);
    return Results.Created($"/api/posts/{post.Id}", ToDto(post));
}).RequireAuthorization();

app.MapPut("/api/posts/{id:guid}", async (
    Guid id,
    UpdatePostRequest request,
    ClaimsPrincipal principal,
    IMongoCollection<PostDocument> posts,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Content)) return Results.BadRequest(new { error = "Post content is required." });

    var update = Builders<PostDocument>.Update
        .Set(p => p.Content, request.Content.Trim())
        .Set(p => p.UpdatedAt, DateTimeOffset.UtcNow);
    var post = await posts.FindOneAndUpdateAsync(
        p => p.Id == id && p.AuthorId == userId,
        update,
        new FindOneAndUpdateOptions<PostDocument> { ReturnDocument = ReturnDocument.After },
        ct);
    if (post is null) return Results.NotFound(new { error = "Post not found." });

    await events.PublishAsync(new PostUpdated(post.Id, post.AuthorId, post.Content, DateTimeOffset.UtcNow), ct);
    return Results.Ok(ToDto(post));
}).RequireAuthorization();

app.MapDelete("/api/posts/{id:guid}", async (
    Guid id,
    ClaimsPrincipal principal,
    IMongoCollection<PostDocument> posts,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();

    var post = await posts.FindOneAndDeleteAsync(p => p.Id == id && p.AuthorId == userId, cancellationToken: ct);
    if (post is null) return Results.NotFound(new { error = "Post not found." });

    await events.PublishAsync(new PostDeleted(post.Id, post.AuthorId, DateTimeOffset.UtcNow), ct);
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/posts/{id:guid}", async (Guid id, IMongoCollection<PostDocument> posts, CancellationToken ct) =>
{
    var post = await posts.Find(p => p.Id == id).FirstOrDefaultAsync(ct);
    return post is null ? Results.NotFound(new { error = "Post not found." }) : Results.Ok(ToDto(post));
});

app.MapGet("/api/posts/by-user/{userId:guid}", async (Guid userId, int limit, int offset, IMongoCollection<PostDocument> posts, CancellationToken ct) =>
{
    var result = await posts.Find(p => p.AuthorId == userId)
        .SortByDescending(p => p.PostedAt)
        .Skip(offset)
        .Limit(limit is <= 0 or > 100 ? 20 : limit)
        .ToListAsync(ct);
    return Results.Ok(result.Select(ToDto));
});

app.MapGet("/api/posts/search", async (string? q, int limit, int offset, IMongoCollection<PostDocument> posts, CancellationToken ct) =>
{
    var filter = string.IsNullOrWhiteSpace(q)
        ? Builders<PostDocument>.Filter.Empty
        : Builders<PostDocument>.Filter.Regex(p => p.Content, new MongoDB.Bson.BsonRegularExpression(q, "i"));
    var result = await posts.Find(filter)
        .SortByDescending(p => p.PostedAt)
        .Skip(offset)
        .Limit(limit is <= 0 or > 100 ? 20 : limit)
        .ToListAsync(ct);
    return Results.Ok(new SearchResultsDto(result.Select(ToDto).ToList(), q ?? "", limit, offset));
});

app.Run();

static PostDto ToDto(PostDocument post) =>
    new(post.Id, post.AuthorId, post.AuthorHandle, post.AuthorDisplayName, post.Content, post.PostedAt, post.UpdatedAt);

public sealed class PostDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid AuthorId { get; set; }
    public string AuthorHandle { get; set; } = "";
    public string AuthorDisplayName { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTimeOffset PostedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record CreatePostRequest(string Content);
public sealed record UpdatePostRequest(string Content);
public sealed record PostDto(Guid PostId, Guid AuthorId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset PostedAt, DateTimeOffset UpdatedAt);
public sealed record SearchResultsDto(List<PostDto> Posts, string Query, int Limit, int Offset);

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}
