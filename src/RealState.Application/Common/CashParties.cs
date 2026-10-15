using Microsoft.EntityFrameworkCore;
using RealState.Application.Entities;
using RealState.Application.Interfaces;

namespace RealState.Application.Common;

/// <summary>
/// The built-in «عميل نقدي» / «مورد نقدي» every tenant has — the default party of cash (نقدي) sales / purchase
/// invoices. Seeded on startup for existing tenants and when a tenant is created; flagged <c>IsDefaultCash</c> and
/// protected from deletion.
/// </summary>
public static class CashParties
{
    public const string CustomerName = "عميل نقدي";
    public const string SupplierName = "مورد نقدي";

    /// <summary>Adds the tenant's cash customer / supplier when missing (the caller saves). Returns true when it added any.</summary>
    public static async Task<bool> EnsureAsync(IApplicationDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        var added = false;
        if (!await db.Customers.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenantId && c.IsDefaultCash && !c.IsDeleted, ct))
        {
            db.Customers.Add(new Customer { TenantId = tenantId, FullName = CustomerName, IsDefaultCash = true, Notes = "عميل افتراضي لفواتير المبيعات النقدية." });
            added = true;
        }
        if (!await db.Suppliers.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenantId && s.IsDefaultCash && !s.IsDeleted, ct))
        {
            db.Suppliers.Add(new Supplier { TenantId = tenantId, Name = SupplierName, IsDefaultCash = true, Notes = "مورد افتراضي لفواتير المشتريات النقدية." });
            added = true;
        }
        return added;
    }
}
