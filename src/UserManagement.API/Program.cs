using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using UserManagement.API.Authorization;
using UserManagement.API.Common;
using UserManagement.API.Filters;
using UserManagement.API.Middleware;
using UserManagement.Application;
using UserManagement.Application.Common.Settings;
using UserManagement.Application.Interfaces;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Interfaces;
using UserManagement.Infrastructure;
using UserManagement.Infrastructure.Persistence;
using UserManagement.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

// Add services to the container.
builder.Services.AddControllers(options => options.Filters.Add<ValidateModelFilter>())
    .ConfigureApiBehaviorOptions(options => options.SuppressModelStateInvalidFilter = true);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserAccessor>();

// Persist Data Protection keys to disk so they survive IIS app pool recycles/restarts.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")))
    .SetApplicationName("UserManagement.API");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
var configuredProfilePictureSettings = builder.Configuration
    .GetSection(ProfilePictureSettings.SectionName)
    .Get<ProfilePictureSettings>() ?? new ProfilePictureSettings();
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = configuredProfilePictureSettings.MaxFileSizeBytes);

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer();

// Configured via the options pattern (rather than a captured local) so that IOptions<JwtSettings> is
// resolved lazily from the final DI container configuration - this keeps test/host config overrides in sync.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<Microsoft.Extensions.Options.IOptions<JwtSettings>>((bearerOptions, jwtOptions) =>
    {
        var settings = jwtOptions.Value;
        bearerOptions.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        bearerOptions.SaveToken = true;
        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(settings.ClockSkewSeconds)
        };
        bearerOptions.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdValue = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var authenticationVersionValue = context.Principal?.FindFirst(AuthClaimTypes.AuthenticationVersion)?.Value;
                if (!Guid.TryParse(userIdValue, out var userId) ||
                    !Guid.TryParse(authenticationVersionValue, out var authenticationVersion))
                {
                    context.Fail("Invalid authentication token.");
                    return;
                }

                var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
                var user = await users.GetByIdAsync(userId, context.HttpContext.RequestAborted);
                if (user is null || !user.IsActive || user.AuthenticationVersion != authenticationVersion)
                    context.Fail("Authentication token has been revoked.");
            }
        };
    });


builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

// Each policy maps to a single fixed permission action. Which *roles* satisfy that permission is
// resolved at login from the database (Role -> Permission assignments, editable via the Roles admin
// screen/API) and carried as "permission" claims on the JWT - so granting an existing or brand-new
// role access to, say, user management never requires touching this file; only adding a genuinely
// new kind of action does.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("UsersRead", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.UsersRead)))
    .AddPolicy("UsersWrite", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.UsersWrite)))
    .AddPolicy("UsersDelete", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.UsersDelete)))
    .AddPolicy("RolesManage", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.RolesManage)))
    .AddPolicy("AuditRead", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.AuditRead)))
    .AddPolicy("DashboardAdminView", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.DashboardAdminView)));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-login", context => CreateClientIpLimiter(context, permitLimit: 100));
    options.AddPolicy("auth-registration", context => CreateClientIpLimiter(context, permitLimit: 5));
    options.AddPolicy("auth-recovery", context => CreateClientIpLimiter(context, permitLimit: 5));
    options.AddPolicy("auth-token", context => CreateClientIpLimiter(context, permitLimit: 20));
});

static RateLimitPartition<string> CreateClientIpLimiter(HttpContext context, int permitLimit)
{
    var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return RateLimitPartition.GetFixedWindowLimiter($"{permitLimit}:{partitionKey}", _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true
    });
}

var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        if (corsSettings.AllowedOrigins.Length > 0)
            policy.WithOrigins(corsSettings.AllowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        else
            policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "User Management API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter 'Bearer {your JWT token}'",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});

var app = builder.Build();

// Apply pending EF Core migrations and seed data automatically on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
    await seeder.SeedAsync();
}

// Configure the HTTP request pipeline.
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "User Management API v1"));
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

var profilePictureSettings = app.Services.GetRequiredService<IOptions<ProfilePictureSettings>>().Value;
var profilePictureStoragePath = Path.GetFullPath(Path.IsPathRooted(profilePictureSettings.StoragePath)
    ? profilePictureSettings.StoragePath
    : Path.Combine(app.Environment.ContentRootPath, profilePictureSettings.StoragePath));
Directory.CreateDirectory(profilePictureStoragePath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(profilePictureStoragePath),
    RequestPath = "/uploads/profile-pictures"
});

app.UseCors("Default");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestampUtc = DateTime.UtcNow }))
    .AllowAnonymous();

app.Run();

public partial class Program { }
