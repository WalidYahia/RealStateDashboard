using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Identity;

namespace RealState.Infrastructure.Persistence;

/// <summary>
/// Seeds the default tenant, security (permissions/role/admin), lookups and a coherent set of sample
/// business data sized so the executive dashboard reproduces the mockup (85% sales/collection, etc.).
/// Idempotent: safe to run on every startup.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        await SeedTenantAsync(db);
        await SeedPermissionsAsync(db);
        await SeedRoleAndAdminAsync(db, roleManager, userManager);
        await SeedLookupsAsync(db);
        await SeedBusinessDataAsync(db, logger);
    }

    private static async Task SeedTenantAsync(ApplicationDbContext db)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == AppConstants.DefaultTenantId))
        {
            db.Tenants.Add(new Tenant
            {
                Id = AppConstants.DefaultTenantId,
                Name = AppConstants.DefaultTenantName,
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedPermissionsAsync(ApplicationDbContext db)
    {
        var existing = await db.Permissions.Select(p => p.Name).ToListAsync();
        var missing = PermissionNames.All.Except(existing).ToList();
        if (missing.Count == 0) return;

        foreach (var name in missing)
        {
            db.Permissions.Add(new Permission
            {
                Name = name,
                DisplayName = name,
                Group = name.Split('.')[0],
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedRoleAndAdminAsync(
        ApplicationDbContext db, RoleManager<ApplicationRole> roleManager, UserManager<ApplicationUser> userManager)
    {
        // Roles are global capability sets; tenant isolation is enforced on users + data filters.
        var superAdmin = await EnsureRoleWithPermissionsAsync(
            db, roleManager, AppConstants.SuperAdminRole,
            "وصول كامل لكل الوحدات والصلاحيات.", PermissionNames.All);

        await EnsureRoleWithPermissionsAsync(
            db, roleManager, AppConstants.TenantAdminRole,
            "إدارة كاملة داخل المؤسسة (بدون إنشاء مؤسسات).", PermissionNames.ForTenantAdmin);

        var role = superAdmin;

        var admin = await userManager.FindByNameAsync(AppConstants.DefaultAdminUserName);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = AppConstants.DefaultAdminUserName,
                Email = AppConstants.DefaultAdminEmail,
                EmailConfirmed = true,
                FullName = "مدير النظام",
                TenantId = AppConstants.DefaultTenantId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };
            await userManager.CreateAsync(admin, AppConstants.DefaultAdminPassword);
        }

        if (!await userManager.IsInRoleAsync(admin, AppConstants.SuperAdminRole))
            await userManager.AddToRoleAsync(admin, AppConstants.SuperAdminRole);
    }

    /// <summary>Creates the role if missing and grants it exactly the requested permissions (adds any missing).</summary>
    private static async Task<ApplicationRole> EnsureRoleWithPermissionsAsync(
        ApplicationDbContext db, RoleManager<ApplicationRole> roleManager,
        string roleName, string description, IReadOnlyList<string> permissionNames)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            role = new ApplicationRole(roleName)
            {
                TenantId = AppConstants.DefaultTenantId,
                Description = description,
            };
            await roleManager.CreateAsync(role);
        }

        var wanted = await db.Permissions.Where(p => permissionNames.Contains(p.Name)).ToListAsync();
        var granted = await db.RolePermissions.Where(rp => rp.RoleId == role.Id).Select(rp => rp.PermissionId).ToListAsync();
        foreach (var perm in wanted.Where(p => !granted.Contains(p.Id)))
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = perm.Id });
        await db.SaveChangesAsync();

        return role;
    }

    private static async Task SeedLookupsAsync(ApplicationDbContext db)
    {
        if (!await db.Currencies.IgnoreQueryFilters().AnyAsync())
        {
            db.Currencies.AddRange(
                new Currency { Name = "جنيه مصري", Code = "EGP", Symbol = "ج.م" },
                new Currency { Name = "دولار أمريكي", Code = "USD", Symbol = "$" });
        }

        if (!await db.Sections.IgnoreQueryFilters().AnyAsync())
        {
            db.Sections.AddRange(
                new Section { Name = "المبيعات" },
                new Section { Name = "التسويق" },
                new Section { Name = "المالية" },
                new Section { Name = "الموارد البشرية" });
        }

        if (!await db.Countries.IgnoreQueryFilters().AnyAsync())
        {
            var egypt = new Country { Name = "مصر", Code = "EG" };
            egypt.Cities.Add(new City { Name = "القاهرة" });
            egypt.Cities.Add(new City { Name = "الجيزة" });
            egypt.Cities.Add(new City { Name = "الإسكندرية" });
            db.Countries.Add(egypt);
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedBusinessDataAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.Projects.IgnoreQueryFilters().AnyAsync()) return; // already seeded

        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var rnd = new Random(128);

        // --- Projects (targets sum to 7,000,000) ---
        var projects = new List<Project>
        {
            new() { Name = "كمبوند زايد 128", Location = "الشيخ زايد", TotalUnits = 320, SoldUnits = 262, TargetAmount = 2_500_000, AchievedAmount = 2_050_000, ProgressPercent = 82 },
            new() { Name = "مشروع بيت الوطن", Location = "التجمع الخامس", TotalUnits = 200, SoldUnits = 126, TargetAmount = 2_000_000, AchievedAmount = 1_260_000, ProgressPercent = 63 },
            new() { Name = "مشروع الحي الثالث", Location = "٦ أكتوبر", TotalUnits = 150, SoldUnits = 61, TargetAmount = 1_500_000, AchievedAmount = 615_000, ProgressPercent = 41 },
            new() { Name = "مول العاصمة الإدارية", Location = "العاصمة الإدارية", TotalUnits = 90, SoldUnits = 25, TargetAmount = 1_000_000, AchievedAmount = 280_000, ProgressPercent = 28 },
        };
        db.Projects.AddRange(projects);

        // --- Customers ---
        var customerNames = new[]
        {
            "أحمد محمد", "منى علي", "خالد حسن", "سارة إبراهيم", "محمود سمير", "هبة فؤاد",
            "عمرو زكي", "ريم عادل", "طارق فهمي", "دينا مصطفى", "يوسف كمال", "نورا صبحي",
        };
        var customers = customerNames.Select((n, i) => new Customer
        {
            FullName = n,
            Phone = $"010{rnd.Next(10000000, 99999999)}",
            Email = $"customer{i + 1}@example.com",
        }).ToList();
        db.Customers.AddRange(customers);

        // --- Leads: 146 this month, grouped by 4 marketing campaigns, weighted toward recent days ---
        var campaigns = new (string Source, int Count)[]
        {
            ("حملة مشروع زايد 128", 52),
            ("حملة بيت الوطن", 44),
            ("حملة الحي الثالث", 28),
            ("حملة العاصمة الإدارية", 22),
        };
        var maxOffset = today.Day - 1; // keep all leads within the current month
        foreach (var (source, count) in campaigns)
        {
            for (var i = 0; i < count; i++)
            {
                // Bias toward recent days so the trend line rises.
                var offset = maxOffset - (int)(maxOffset * Math.Sqrt(rnd.NextDouble()));
                var created = today.AddDays(-offset).AddHours(rnd.Next(8, 20)).AddMinutes(rnd.Next(60));
                db.Leads.Add(new Lead
                {
                    FullName = $"عميل محتمل {source[^2..]} {i + 1}",
                    Phone = $"011{rnd.Next(10000000, 99999999)}",
                    Source = source,
                    Status = (LeadStatus)rnd.Next(0, 6),
                    EstimatedValue = rnd.Next(200, 2000) * 1000,
                    CreatedAt = created,
                });
            }
        }

        // --- Sales invoices (non-reservation), all fully paid. Monthly total = 5,920,000 ---
        int inv = 1000;
        SalesInvoice Sale(decimal amount, DateTime date, Guid customerId, Guid projectId, bool reservation = false) => new()
        {
            Number = $"INV-{inv++}",
            CustomerId = customerId,
            ProjectId = projectId,
            Amount = amount,
            PaidAmount = reservation ? 0 : amount,
            Status = reservation ? InvoiceStatus.Confirmed : InvoiceStatus.Paid,
            InvoiceDate = date,
            IsReservation = reservation,
            CreatedAt = date,
        };

        Guid Cust() => customers[rnd.Next(customers.Count)].Id;
        Guid Proj() => projects[rnd.Next(projects.Count)].Id;

        // Today's sales -> 3,250,000
        db.SalesInvoices.Add(Sale(1_500_000, today.AddHours(9), Cust(), Proj()));
        db.SalesInvoices.Add(Sale(1_000_000, today.AddHours(11), Cust(), Proj()));
        db.SalesInvoices.Add(Sale(750_000, today.AddHours(13), Cust(), Proj()));

        // Earlier this month -> 2,670,000 (monthly non-reservation total = 5,920,000)
        var earlier = new decimal[] { 600_000, 520_000, 450_000, 400_000, 380_000, 320_000 };
        for (int i = 0; i < earlier.Length; i++)
        {
            var day = monthStart.AddDays(rnd.Next(0, Math.Max(1, maxOffset)));
            db.SalesInvoices.Add(Sale(earlier[i], day.AddHours(rnd.Next(9, 17)), Cust(), Proj()));
        }

        // --- Reservations: 18 today ---
        for (int i = 0; i < 18; i++)
            db.SalesInvoices.Add(Sale(50_000, today.AddHours(rnd.Next(9, 18)), Cust(), Proj(), reservation: true));

        // --- Tasks: 39 total (34 completed, 4 in progress, 1 overdue) ---
        for (int i = 0; i < 34; i++)
            db.Tasks.Add(new TaskItem { Title = $"مهمة منجزة {i + 1}", State = TaskState.Completed, CompletedAt = today.AddHours(-rnd.Next(1, 8)), DueDate = today });
        for (int i = 0; i < 4; i++)
            db.Tasks.Add(new TaskItem { Title = $"مهمة قيد التنفيذ {i + 1}", State = TaskState.InProgress, DueDate = today.AddDays(1) });
        db.Tasks.Add(new TaskItem { Title = "متابعة عقد متأخرة", State = TaskState.Overdue, DueDate = today.AddDays(-2) });

        // --- Notifications: 4 urgent + 2 complaints + 11 info (all unread) + 15 read ---
        void Notify(string title, string? msg, NotificationLevel level, bool read, int minutesAgo)
            => db.Notifications.Add(new Notification
            {
                Title = title, Message = msg, Level = level, IsRead = read,
                Timestamp = DateTime.Now.AddMinutes(-minutesAgo),
            });

        Notify("تم استحقاق دفعة للعميل أحمد محمد", "دفعة مستحقة اليوم", NotificationLevel.Urgent, false, 10);
        Notify("عقد جديد في مشروع زايد 128", "تم توقيع عقد جديد", NotificationLevel.Urgent, false, 45);
        Notify("تم إضافة Lead جديد", "عميل محتمل جديد من حملة بيت الوطن", NotificationLevel.Urgent, false, 60);
        Notify("تنبيه تحصيل متأخر", "قسط متأخر عن السداد", NotificationLevel.Urgent, false, 90);
        Notify("شكوى عميل مفتوحة", "شكوى بخصوص التسليم", NotificationLevel.Warning, false, 120);
        Notify("شكوى عميل مفتوحة", "استفسار عن التعاقد", NotificationLevel.Warning, false, 150);
        for (int i = 0; i < 11; i++) Notify($"طلب خدمة عملاء جديد {i + 1}", "طلب جديد", NotificationLevel.Info, false, 30 + i * 7);
        for (int i = 0; i < 15; i++) Notify($"طلب مغلق {i + 1}", "تمت المعالجة", NotificationLevel.Info, true, 200 + i * 15);

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded sample business data for the executive dashboard.");
    }
}
