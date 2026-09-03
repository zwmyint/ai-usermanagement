using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Serilog;
using UserManagement.UI.Configuration;
using UserManagement.UI.Security;
using UserManagement.UI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection(ApiSettings.SectionName));
var apiBaseUrl = builder.Configuration.GetSection(ApiSettings.SectionName)["BaseUrl"] ?? "http://localhost:5000/";

// Persist Data Protection keys to disk so the auth cookie survives IIS app pool recycles/restarts.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")))
    .SetApplicationName("UserManagement.UI");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ApiAuthTokenHandler>();

// In Development, the API's self-signed HTTPS dev certificate is typically untrusted on Linux/WSL
// hosts (no equivalent to "dotnet dev-certs https --trust"). Bypass certificate validation for the
// UI -> API HttpClient only in Development so local HTTPS-to-HTTPS calls succeed; Production keeps
// full certificate validation.
static void ConfigurePrimaryHandler(IServiceProvider sp, HttpClientHandler handler)
{
    if (sp.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
    {
        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    }
}

// "ApiRaw" is used for anonymous calls (login/register/refresh) and must never carry stale bearer tokens.
builder.Services.AddHttpClient("ApiRaw", client => client.BaseAddress = new Uri(apiBaseUrl))
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var handler = new HttpClientHandler();
        ConfigurePrimaryHandler(sp, handler);
        return handler;
    });

// "Api" automatically attaches (and refreshes) the signed-in user's JWT via ApiAuthTokenHandler.
builder.Services.AddHttpClient("Api", client => client.BaseAddress = new Uri(apiBaseUrl))
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var handler = new HttpClientHandler();
        ConfigurePrimaryHandler(sp, handler);
        return handler;
    })
    .AddHttpMessageHandler<ApiAuthTokenHandler>();

builder.Services.AddScoped<IAuthApiService, AuthApiService>();
builder.Services.AddScoped<IProfileApiService, ProfileApiService>();
builder.Services.AddScoped<IUserApiService, UserApiService>();
builder.Services.AddScoped<IRoleApiService, RoleApiService>();
builder.Services.AddScoped<IAuditLogApiService, AuditLogApiService>();
builder.Services.AddScoped<IDashboardApiService, DashboardApiService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("UsersRead", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.UsersRead)))
    .AddPolicy("UsersWrite", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.UsersWrite)))
    .AddPolicy("UsersDelete", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.UsersDelete)))
    .AddPolicy("RolesManage", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.RolesManage)))
    .AddPolicy("AuditRead", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.AuditRead)));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
