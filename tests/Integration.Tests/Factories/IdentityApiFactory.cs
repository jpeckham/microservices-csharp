extern alias IdentityApi;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

public sealed class IdentityApiFactory(string mongoConnectionString)
    : WebApplicationFactory<IdentityApi::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Mongo:ConnectionString", mongoConnectionString);
        builder.UseSetting("Mongo:DatabaseName", "test_identity");
        builder.UseSetting("ServiceBus:ConnectionString", "");
        builder.UseSetting("Messaging:LocalEventSinkUrl", "");
    }
}
