using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Entities;
using RealState.Application.Interfaces;

namespace RealState.Application.Inventory;

/// <summary>One unit level of a product: 1 = smallest (stock/cost unit), 2 = bigger, 3 = biggest.
/// <see cref="Factor"/> = how many smallest units make one of this unit; <see cref="SalePrice"/> = the product's selling
/// price for one of this unit (0 = none set).</summary>
public sealed record ProductUnitLevel(byte Level, Guid? UnitId, string Name, decimal Factor, decimal SalePrice = 0m);

/// <summary>
/// A product's units (up to 3 levels) — stock, movements and costs are always in the smallest unit (level 1);
/// documents may be entered in any level and are converted with <see cref="ProductUnitLevel.Factor"/>.
/// </summary>
public sealed class ProductUnitSet
{
    /// <summary>Name shown when a product has no unit of measure set.</summary>
    public const string NoUnitName = "وحدة";

    public ProductUnitSet(IReadOnlyList<ProductUnitLevel> levels, byte defaultLevel)
    {
        Levels = levels.Count > 0 ? levels : new[] { new ProductUnitLevel(1, null, NoUnitName, 1m) };
        DefaultLevel = Levels.Any(l => l.Level == defaultLevel) ? defaultLevel : (byte)1;
    }

    public IReadOnlyList<ProductUnitLevel> Levels { get; }
    public byte DefaultLevel { get; }
    public ProductUnitLevel Base => Levels[0];
    public ProductUnitLevel Default => Get(DefaultLevel);
    public bool IsMulti => Levels.Count > 1;

    /// <summary>The level, falling back to the smallest unit when the product doesn't define it.</summary>
    public ProductUnitLevel Get(byte? level) => Levels.FirstOrDefault(l => l.Level == level) ?? Base;

    /// <summary>Quantity in the smallest unit → in the given level's unit.</summary>
    public decimal FromBase(decimal baseQty, byte? level) => baseQty / Get(level).Factor;

    private static string Q(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// A smallest-unit quantity spelled out in the product's units, biggest first — e.g. 2300050 سم with
    /// متر = 100 سم and كيلو = 1000 متر → «2 كيلو، 300 متر، 50 سم». Single-unit products → «50 سم».
    /// </summary>
    public string Breakdown(decimal baseQty)
    {
        if (!IsMulti || baseQty == 0) return $"{Q(baseQty)} {Base.Name}";
        var sign = baseQty < 0 ? "-" : "";
        var rest = Math.Abs(baseQty);
        var parts = new List<string>();
        foreach (var l in Levels.OrderByDescending(l => l.Level))
        {
            if (l.Level == 1) { if (rest > 0) parts.Add($"{Q(rest)} {l.Name}"); break; }
            var n = Math.Floor(rest / l.Factor);
            if (n > 0) { parts.Add($"{Q(n)} {l.Name}"); rest -= n * l.Factor; }
        }
        return sign + string.Join("، ", parts);
    }

    /// <summary>Compact chain for lists, e.g. «سم ← متر (100) ← كيلو (1000)» (factors relative to the previous level).</summary>
    public string Chain()
    {
        var parts = new List<string> { Base.Name };
        for (var i = 1; i < Levels.Count; i++)
            parts.Add($"{Levels[i].Name} ({Q(Levels[i].Factor / Levels[i - 1].Factor)})");
        return string.Join(" ← ", parts);
    }

    /// <summary>The levels for a line's unit picker: [{"l":1,"n":"سم","f":1,"p":sale price}, …] (+ default level as "d").</summary>
    public string ToJson() => JsonSerializer.Serialize(new
    {
        d = DefaultLevel,
        u = Levels.Select(l => new { l = l.Level, n = l.Name, f = l.Factor, p = l.SalePrice })
    });
}

public static class ProductUnits
{
    /// <summary>Builds a product's unit set from its unit fields and the unit names.</summary>
    public static ProductUnitSet Build(Product p, IReadOnlyDictionary<Guid, string> unitNames)
    {
        string Name(Guid? id) => id is Guid g && unitNames.TryGetValue(g, out var n) ? n : ProductUnitSet.NoUnitName;
        var levels = new List<ProductUnitLevel> { new(1, p.UnitOfMeasureId, Name(p.UnitOfMeasureId), 1m, p.SalePrice) };
        if (p.UnitOfMeasureId is not null && p.Unit2Id is not null && p.Unit2Factor > 0)
        {
            levels.Add(new(2, p.Unit2Id, Name(p.Unit2Id), p.Unit2Factor, p.SalePrice2));
            if (p.Unit3Id is not null && p.Unit3Factor > 0)
                levels.Add(new(3, p.Unit3Id, Name(p.Unit3Id), p.Unit2Factor * p.Unit3Factor, p.SalePrice3));
        }
        return new ProductUnitSet(levels, p.DefaultUnitLevel);
    }

    /// <summary>Unit sets for the given products (all products when <paramref name="productIds"/> is null).
    /// Soft-deleted / inactive products are included so history still resolves.</summary>
    public static async Task<Dictionary<Guid, ProductUnitSet>> LoadAsync(IApplicationDbContext db, IEnumerable<Guid>? productIds, CancellationToken ct)
    {
        var q = db.Products.AsQueryable();
        if (productIds is not null)
        {
            var ids = productIds.Distinct().ToList();
            q = q.Where(p => ids.Contains(p.Id));
        }
        var products = await q.ToListAsync(ct);
        var names = await db.UnitsOfMeasure.ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return products.ToDictionary(p => p.Id, p => Build(p, names));
    }

    /// <summary>Unit set of a product from a loaded map, or a plain single-unit set when it's missing.</summary>
    public static ProductUnitSet Of(this IReadOnlyDictionary<Guid, ProductUnitSet> map, Guid productId)
        => map.TryGetValue(productId, out var s) ? s : new ProductUnitSet(Array.Empty<ProductUnitLevel>(), 1);
}
