using System.Globalization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using RealState.Application;
using RealState.Application.Common;
using RealState.Application.Identity;
using RealState.Application.Interfaces;
using RealState.Infrastructure;
using RealState.Infrastructure.Persistence;
using RealState.Web.Security;
using RealState.Web.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog: console + rolling file, configured from appsettings.
builder.Host.UseSerilog((context, services, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// Application + Infrastructure layers.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ASP.NET Identity with Guid keys and permission-aware claims.
builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        // Only a minimum length is required — no character-class rules.
        options.Password.RequiredLength = 8;
        options.Password.RequiredUniqueChars = 1;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        // Email is optional; uniqueness is enforced in the user controller when one is provided.
        options.User.RequireUniqueEmail = false;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<PermissionClaimsPrincipalFactory>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    // The static host user has no database row, so the default security-stamp validation would
    // reject its cookie. Skip validation for the host; database-backed users still get validated.
    options.Events.OnValidatePrincipal = context =>
    {
        if (context.Principal?.HasClaim(AppConstants.HostClaimType, "true") == true)
            return Task.CompletedTask;
        return SecurityStampValidator.ValidatePrincipalAsync(context);
    };
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// One authorization policy per permission; matched by the "permission" claim.
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in PermissionNames.All)
        options.AddPolicy(permission, policy => policy.RequireClaim("permission", permission));
});

builder.Services.AddControllersWithViews(options =>
{
    // Central try/catch → friendly SweetAlert, and an audit trail for every user action.
    options.Filters.Add<RealState.Web.Filters.GlobalExceptionFilter>();
    options.Filters.Add<RealState.Web.Filters.ActivityLogFilter>();
});

// Arabic RTL as the only supported culture.
var arabic = new CultureInfo("ar-EG");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(arabic);
    options.SupportedCultures = new List<CultureInfo> { arabic };
    options.SupportedUICultures = new List<CultureInfo> { arabic };
});

var app = builder.Build();

// Apply migrations and seed on startup.
using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        // Surface the real cause in the log stream instead of an opaque SIGABRT (exit 134).
        // Most common on Azure: the app can't reach Azure SQL — check the connection string,
        // the SQL server firewall ("Allow Azure services…" = ON) and that the database exists.
        startupLogger.LogCritical(ex, "Startup database migration/seeding failed: {Message}", ex.Message);
        throw;
    }
}

// Behind the Azure App Service reverse proxy: trust X-Forwarded-Proto/For so HTTPS redirection,
// secure cookies and generated URLs use the real (https) scheme. Must run before other middleware.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeaders.KnownNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRequestLocalization();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
