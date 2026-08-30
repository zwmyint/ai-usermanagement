using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UserManagement.Infrastructure.Persistence;

namespace UserManagement.API.IntegrationTests;

/// <summary>
/// Spins up the full API host against a unique on-disk SQLite database per test class so that
/// migrations and seeding run exactly as they do in production, without tests interfering with each other.
/// </summary>
public class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(
        AppContext.BaseDirectory, "test-dbs", $"{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
                ["Jwt:Key"] = "integration-test-signing-key-with-32-bytes-min",
                ["Smtp:PickupDirectoryOnly"] = "true",
                ["Seed:Enabled"] = "true",
                ["Seed:AdminEmail"] = "admin@example.com",
                ["Seed:AdminPassword"] = "ChangeMe123!"
            });
        });

        // The app builds its DbContext from configuration during WebApplicationBuilder construction, before the
        // ConfigureAppConfiguration overrides above are visible to it, so the connection string is re-applied here.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(
                $"Data Source={_dbPath}",
                sqlite => sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        foreach (var file in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (IOException) { /* best effort cleanup */ }
        }
    }
}
