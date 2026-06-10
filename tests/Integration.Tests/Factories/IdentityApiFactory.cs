extern alias IdentityApi;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

public sealed class IdentityApiFactory(string mongoConnectionString, SocialApiFactory? socialFactory = null)
    : WebApplicationFactory<IdentityApi::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Mongo:ConnectionString", mongoConnectionString);
        builder.UseSetting("Mongo:DatabaseName", "test_identity");
        builder.UseSetting("ServiceBus:ConnectionString", "");
        builder.UseSetting("Messaging:LocalEventSinkUrl", "");

        if (socialFactory is not null)
        {
            builder.UseSetting("Social:ApiUrl", "http://social-api/");
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient("SocialApi")
                    .ConfigurePrimaryHttpMessageHandler(() => socialFactory.Server.CreateHandler());
            });
        }
    }
}
