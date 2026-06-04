namespace Architecture.Tests;

public sealed class SolutionArchitectureTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Solution_contains_required_deployable_projects()
    {
        string[] expectedProjects =
        [
            "src/Identity.Api/Identity.Api.csproj",
            "src/Post.Api/Post.Api.csproj",
            "src/Social.Api/Social.Api.csproj",
            "src/Engagement.Api/Engagement.Api.csproj",
            "src/Feed.Api/Feed.Api.csproj",
            "src/Social.Web/Social.Web.csproj",
            "src/Shared.Contracts/Shared.Contracts.csproj"
        ];

        foreach (var project in expectedProjects)
        {
            Assert.True(File.Exists(Path.Combine(Root, project)), $"{project} should exist.");
        }
    }

    [Fact]
    public void Api_services_use_mongodb_driver_directly()
    {
        foreach (var service in new[] { "Identity.Api", "Post.Api", "Social.Api", "Engagement.Api", "Feed.Api" })
        {
            var program = File.ReadAllText(Path.Combine(Root, "src", service, "Program.cs"));
            Assert.Contains("MongoClient", program);
            Assert.Contains("IMongoCollection", program);
        }
    }

    [Fact]
    public void Implementation_does_not_add_repository_or_domain_project_patterns()
    {
        var forbiddenFolders = Directory.GetDirectories(Path.Combine(Root, "src"), "*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith($"{Path.DirectorySeparatorChar}Domain", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith($"{Path.DirectorySeparatorChar}Repositories", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(forbiddenFolders);
    }

    [Fact]
    public void Docker_compose_defines_local_mongo_and_service_bus_emulator()
    {
        var compose = File.ReadAllText(Path.Combine(Root, "docker-compose.yml"));

        Assert.Contains("mongo:7", compose);
        Assert.Contains("mcr.microsoft.com/azure-messaging/servicebus-emulator:latest", compose);
        Assert.Contains("IdentityDb", compose);
        Assert.Contains("PostDb", compose);
        Assert.Contains("SocialDb", compose);
        Assert.Contains("EngagementDb", compose);
        Assert.Contains("FeedDb", compose);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MicroservicesSocial.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
