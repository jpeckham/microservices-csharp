extern alias EngagementApi;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

public sealed class EngagementApiFactory(string mongoConnectionString)
    : WebApplicationFactory<EngagementApi::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Mongo:ConnectionString", mongoConnectionString);
        builder.UseSetting("Mongo:DatabaseName", "test_engagement");
        builder.UseSetting("ServiceBus:ConnectionString", "");
        builder.UseSetting("Messaging:LocalEventSinkUrl", "");
    }
}
