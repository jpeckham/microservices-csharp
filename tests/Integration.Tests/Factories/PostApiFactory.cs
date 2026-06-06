extern alias PostApi;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

public sealed class PostApiFactory(string mongoConnectionString)
    : WebApplicationFactory<PostApi::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Mongo:ConnectionString", mongoConnectionString);
        builder.UseSetting("Mongo:DatabaseName", "test_posts");
        builder.UseSetting("ServiceBus:ConnectionString", "");
        builder.UseSetting("Messaging:LocalEventSinkUrl", "");
    }
}
