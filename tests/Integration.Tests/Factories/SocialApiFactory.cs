extern alias SocialApi;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

public sealed class SocialApiFactory(string mongoConnectionString)
    : WebApplicationFactory<SocialApi::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Mongo:ConnectionString", mongoConnectionString);
        builder.UseSetting("Mongo:DatabaseName", "test_social");
        builder.UseSetting("ServiceBus:ConnectionString", "");
        builder.UseSetting("Messaging:LocalEventSinkUrl", "");
    }
}
