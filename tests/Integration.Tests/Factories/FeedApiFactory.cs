extern alias FeedApi;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

public sealed class FeedApiFactory(string mongoConnectionString)
    : WebApplicationFactory<FeedApi::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Mongo:ConnectionString", mongoConnectionString);
        builder.UseSetting("Mongo:DatabaseName", "test_feed");
        builder.UseSetting("ServiceBus:ConnectionString", "");
        builder.UseSetting("Messaging:LocalEventSinkUrl", "");
    }
}
