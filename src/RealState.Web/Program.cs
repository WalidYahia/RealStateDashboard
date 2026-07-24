using System.Globalization;
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
        options.User.RequireUniqueEmail = true;
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
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// One authorization policy per permission; matched by the "permission" claim.
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in PermissionNames.All)
        options.AddPolicy(permission, policy => policy.RequireClaim("permission", permission));
});

builder.Services.AddControllersWithViews();

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
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

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
