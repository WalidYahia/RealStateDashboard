using Microsoft.EntityFrameworkCore;
using RealState.Application.Interfaces;
using RealState.Application.Inventory;
using RealState.Web.Areas.Inventory.Models;

namespace RealState.Web.Areas.Inventory.Controllers;

/// <summary>
/// Unit handling shared by the inventory document screens (receipt / issue / transfer / adjustment / count).
/// Lines are entered in any of the product's units; they're stored — and posted by the engine — in the smallest unit,
/// with the chosen unit's level, factor and name kept on the line for display and editing.
/// </summary>
internal static class InvDocUnits
{
    /// <summary>Active stock-tracked products for a line picker, each with its units.</summary>
    public static async Task<List<ProductPick>> PicksAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var products = await db.Products.Where(p => p.IsActive && p.TrackInventory).OrderBy(p => p.Sku).ToListAsync(ct);
        var names = await db.UnitsOfMeasure.ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return products.Select(p =>
        {
            var set = ProductUnits.Build(p, names);
            return new ProductPick { Value = p.Id.ToString(), Text = p.Sku + " — " + p.Name, ProductId = p.Id, Units = set, UnitsJson = set.ToJson() };
        }).ToList();
    }

    /// <summary>The unit a line is in: its chosen level, or the product's default when none was chosen (0).</summary>
    public static ProductUnitLevel Resolve(IReadOnlyDictionary<Guid, ProductUnitSet> units, Guid productId, byte level)
    {
        var set = units.Of(productId);
        return level == 0 ? set.Default : set.Get(level);
    }

    /// <summary>A smallest-unit quantity expressed in the line's unit.</summary>
    public static decimal Entered(decimal baseQty, decimal factor) => factor > 0 ? Math.Round(baseQty / factor, 4) : baseQty;

    /// <summary>A smallest-unit cost expressed per the line's unit.</summary>
    public static decimal EnteredCost(decimal baseCost, decimal factor) => factor > 0 ? Math.Round(baseCost * factor, 4) : baseCost;
}
