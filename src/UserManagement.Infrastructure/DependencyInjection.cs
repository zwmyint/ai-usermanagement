using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UserManagement.Application.Common.Settings;
using UserManagement.Application.Interfaces;
using UserManagement.Domain.Interfaces;
using UserManagement.Infrastructure.Email;
using UserManagement.Infrastructure.Persistence;
using UserManagement.Infrastructure.Persistence.Repositories;
using UserManagement.Infrastructure.Persistence.Seed;
using UserManagement.Infrastructure.Security;
using UserManagement.Infrastructure.Services;
using UserManagement.Infrastructure.Storage;

namespace UserManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<DatabaseSettings>(configuration.GetSection(DatabaseSettings.SectionName));
        services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionName));
        services.Configure<SecuritySettings>(configuration.GetSection(SecuritySettings.SectionName));
        services.Configure<SeedSettings>(configuration.GetSection(SeedSettings.SectionName));
        services.Configure<CorsSettings>(configuration.GetSection(CorsSettings.SectionName));
        services.Configure<ProfilePictureSettings>(configuration.GetSection(ProfilePictureSettings.SectionName));

        var useConnection = configuration.GetValue<string>($"{DatabaseSettings.SectionName}:UseConnection")
            ?? "DefaultConnectionSQLite";
        var normalizedConnection = useConnection.Trim().ToLowerInvariant();
        var (provider, connectionName) = normalizedConnection switch
        {
            "defaultconnectionsqlite" => ("sqlite", "Sqlite"),
            "defaultconnectionpostgresql" => ("postgresql", "PostgreSql"),
            _ => throw new InvalidOperationException(
                $"Unsupported Database:UseConnection value '{useConnection}'. Supported values are 'DefaultConnectionSQLite' and 'DefaultConnectionPostgreSQL'.")
        };
        
        var connectionString = configuration.GetValue<string>($"{DatabaseSettings.SectionName}:Connections:{useConnection}")
            ?? configuration.GetConnectionString(connectionName);

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Database:Connections:{useConnection} or ConnectionStrings:{connectionName} must be configured.");

        services.AddDbContext<AppDbContext>(options =>
        {
            if (connectionName == "Sqlite")
            {
                options.UseSqlite(connectionString, sqlite =>
                    sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
            }
            else
            {
                options.UseNpgsql(connectionString, postgres =>
                    postgres.MigrationsAssembly("UserManagement.Infrastructure.PostgreSqlMigrations"));
            }
        });

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IProfilePictureStorage, ProfilePictureStorage>();
        services.AddScoped<DbSeeder>();

        return services;
    }
}
