using Social.Web.Components;
using Social.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<SessionState>();
builder.Services.AddHttpClient<AuthApi>(client => client.BaseAddress = new Uri(builder.Configuration["Services:Identity"] ?? "http://localhost:5101/"));
builder.Services.AddHttpClient<PostApi>(client => client.BaseAddress = new Uri(builder.Configuration["Services:Post"] ?? "http://localhost:5102/"));
builder.Services.AddHttpClient<SocialApi>(client => client.BaseAddress = new Uri(builder.Configuration["Services:Social"] ?? "http://localhost:5103/"));
builder.Services.AddHttpClient<EngagementApi>(client => client.BaseAddress = new Uri(builder.Configuration["Services:Engagement"] ?? "http://localhost:5104/"));
builder.Services.AddHttpClient<FeedApi>(client => client.BaseAddress = new Uri(builder.Configuration["Services:Feed"] ?? "http://localhost:5105/"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
