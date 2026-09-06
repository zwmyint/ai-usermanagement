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

        var provider = configuration.GetValue<string>($"{DatabaseSettings.SectionName}:Provider")
            ?? DatabaseSettings.SqliteProvider;
        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var connectionName = normalizedProvider switch
        {
            "sqlite" => "Sqlite",
            "postgresql" or "postgres" => "PostgreSql",
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{provider}'. Supported values are 'Sqlite' and 'PostgreSql'.")
        };
        var connectionString = configuration.GetConnectionString(connectionName)
            ?? (connectionName == "Sqlite" ? configuration.GetConnectionString("DefaultConnection") : null);

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"ConnectionStrings:{connectionName} must be configured when Database:Provider is '{provider}'.");

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
