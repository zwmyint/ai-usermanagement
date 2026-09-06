using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UserManagement.Infrastructure;
using UserManagement.Infrastructure.Persistence;

namespace UserManagement.Infrastructure.UnitTests;

public class DatabaseProviderRegistrationTests
{
    [Fact]
    public void AddInfrastructure_UsesSqliteByDefault()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Sqlite"] = "Data Source=:memory:"
            })
            .Build());

        using var provider = services.BuildServiceProvider();
        using var db = provider.GetRequiredService<AppDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
    }

    [Fact]
    public void AddInfrastructure_UsesPostgreSqlWhenConfigured()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSql",
                ["ConnectionStrings:PostgreSql"] =
                    "Host=localhost;Port=5432;Database=usermanagement;Username=test;Password=test"
            })
            .Build());

        using var provider = services.BuildServiceProvider();
        using var db = provider.GetRequiredService<AppDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
        Assert.Contains(db.Database.GetMigrations(), migration => migration.Contains("InitialPostgreSql"));
    }

    [Fact]
    public void AddInfrastructure_RejectsUnsupportedProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "MySql",
                ["ConnectionStrings:MySql"] = "server=localhost"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains("Unsupported database provider", exception.Message);
    }

    [Fact]
    public void AddInfrastructure_RejectsMissingSelectedConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSql"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains("ConnectionStrings:PostgreSql", exception.Message);
    }
}
