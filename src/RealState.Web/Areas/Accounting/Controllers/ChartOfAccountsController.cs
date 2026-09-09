using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Accounting.Models;

namespace RealState.Web.Areas.Accounting.Controllers;

[Area("Accounting")]
[Authorize(Policy = PermissionNames.AccountsView)]
public class ChartOfAccountsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountingEngine _engine;
    public ChartOfAccountsController(IApplicationDbContext db, IAccountingEngine engine) { _db = db; _engine = engine; }

    private bool CanManage() => User.HasClaim("permission", PermissionNames.AccountsManage);

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await _engine.EnsureChartAsync(ct);
        var accounts = await _db.Accounts.OrderBy(a => a.SortOrder).ThenBy(a => a.Code).ToListAsync(ct);

        // Net (debit − credit) per account from posted lines, then roll up to ancestors.
        var raw = (await _db.JournalLines.GroupBy(l => l.AccountId)
                .Select(g => new { g.Key, Net = g.Sum(x => x.Debit - x.Credit) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Net);

        // ToLookup tolerates a null key (root accounts) and returns an empty sequence for missing keys.
        var byParent = accounts.ToLookup(a => a.ParentId);
        // Built-in (seeded) + subsidiary accounts are locked — no edit/delete, and their code is fixed.
        var builtIn = LedgerAccounts.Defaults.Select(d => d.Code).ToHashSet();

        List<AccountNode> Build(Guid? parentId, int depth)
        {
            var nodes = new List<AccountNode>();
            foreach (var a in byParent[parentId])
            {
                var node = new AccountNode
                {
                    Id = a.Id, Code = a.Code, Name = a.Name, Type = a.Type, Depth = depth,
                    IsPostable = a.IsPostable, IsActive = a.IsActive,
                    IsSystem = a.SubKind != null || builtIn.Contains(a.Code),
                    IsControlManaged = LedgerAccounts.SubsidiaryControls.Contains(a.Code),
                    IsSubsidiary = a.SubKind != null,
                    Children = Build(a.Id, depth + 1)
                };
                var subtreeNet = raw.GetValueOrDefault(a.Id) + node.Children.Sum(c => c.RawNet);
                node.RawNet = subtreeNet;
                node.Balance = a.IsDebitNormal ? subtreeNet : -subtreeNet;
                nodes.Add(node);
            }
            return nodes;
        }

        var vm = new ChartVm { Roots = Build(null, 0), CanManage = CanManage() };
        return View(vm);
    }

    /// <summary>Suggests the next hierarchical code under a parent (e.g. 1000→1100, 1100→1110).</summary>
    [HttpGet]
    public async Task<IActionResult> NextCode(Guid? parentId, CancellationToken ct)
        => Json(new { code = await NextCodeAsync(parentId, ct) });

    private async Task<string> NextCodeAsync(Guid? parentId, CancellationToken ct)
    {
        var siblingCodes = await _db.Accounts.Where(a => a.ParentId == parentId).Select(a => a.Code).ToListAsync(ct);
        int ToNum(string c) => int.TryParse(c, out var n) ? n : 0;

        if (parentId is null)
        {
            var maxRoot = siblingCodes.Select(ToNum).DefaultIfEmpty(0).Max();
            return (maxRoot <= 0 ? 1000 : maxRoot + 1000).ToString();
        }

        var parent = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == parentId, ct);
        if (parent is null) return string.Empty;
        if (!int.TryParse(parent.Code, out var pnum) || pnum <= 0)
            return $"{parent.Code}-{siblingCodes.Count + 1}";   // non-numeric parent (e.g. subsidiary)

        // Step at the next significant digit below the parent (based on its trailing zeros).
        int tz = 0, t = pnum;
        while (t % 10 == 0) { tz++; t /= 10; }
        var step = tz > 0 ? (int)Math.Pow(10, tz - 1) : 1;

        var childNums = siblingCodes.Select(ToNum).Where(n => n > 0).ToList();
        var next = (childNums.Count > 0 ? childNums.Max() : pnum) + step;
        while (await _db.Accounts.AnyAsync(a => a.Code == next.ToString(), ct)) next += step;
        return next.ToString();
    }

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, Guid? parentId, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var model = new AccountFormModel { ParentAccounts = await ParentOptionsAsync(id, ct), ParentId = parentId };
        if (id is not null)
        {
            var a = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return NotFound();
            if (IsLocked(a)) return Content("<div style=\"padding:18px;color:var(--warning);text-align:center;\">لا يمكن تعديل حساب رئيسي / مُدار من النظام.</div>", "text/html");
            model.Id = a.Id; model.Code = a.Code; model.Name = a.Name; model.Type = a.Type;
            model.ParentId = a.ParentId; model.IsPostable = a.IsPostable; model.IsActive = a.IsActive;
        }
        else if (parentId is Guid pid)
        {
            // New child: pre-fill the auto-generated code + inherit the parent's type.
            model.Code = await NextCodeAsync(pid, ct);
            var parent = await _db.Accounts.Where(a => a.Id == pid).Select(a => (AccountType?)a.Type).FirstOrDefaultAsync(ct);
            if (parent is AccountType pt) model.Type = pt;
        }
        return PartialView("_AccountForm", model);
    }

    private static bool IsLocked(Account a) => a.SubKind != null || LedgerAccounts.Defaults.Any(d => d.Code == a.Code);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(AccountFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        model.Code = model.Code?.Trim() ?? string.Empty;
        // A child inherits its parent's account type; subsidiaries of control accounts can't be hand-added.
        if (model.ParentId is Guid pid2)
        {
            var parent = await _db.Accounts.Where(a => a.Id == pid2).Select(a => new { a.Code, a.Type }).FirstOrDefaultAsync(ct);
            if (parent != null)
            {
                model.Type = parent.Type;   // النوع = نوع الحساب الأب
                if (LedgerAccounts.SubsidiaryControls.Contains(parent.Code))
                    ModelState.AddModelError(nameof(model.ParentId), "لا يمكن إضافة حساب فرعي هنا — يُضاف من صفحته المخصّصة (العملاء/الموردون/المقاولون/الخزائن/الموظفون).");
            }
        }

        // Code is auto-generated for a new account and fixed thereafter — regenerate if missing/duplicate.
        if (model.Id == Guid.Empty && (string.IsNullOrWhiteSpace(model.Code) || await _db.Accounts.AnyAsync(a => a.Code == model.Code, ct)))
            model.Code = await NextCodeAsync(model.ParentId, ct);
        if (!ModelState.IsValid) { model.ParentAccounts = await ParentOptionsAsync(model.Id, ct); return PartialView("_AccountForm", model); }

        if (model.Id == Guid.Empty)
        {
            var maxSort = await _db.Accounts.Where(a => a.ParentId == model.ParentId).MaxAsync(a => (int?)a.SortOrder, ct) ?? 0;
            _db.Accounts.Add(new Account
            {
                Code = model.Code, Name = model.Name, Type = model.Type, ParentId = model.ParentId,
                IsPostable = model.IsPostable, IsActive = model.IsActive, SortOrder = maxSort + 1
            });
            TempData["StatusMessage"] = $"تمت إضافة الحساب {model.Code} — {model.Name}.";
        }
        else
        {
            var a = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (a is null) return NotFound();
            if (IsLocked(a)) return Json(new { ok = false, error = "لا يمكن تعديل حساب رئيسي / مُدار من النظام." });
            // Code stays fixed; parent/type/postable can change for user accounts.
            a.Name = model.Name; a.IsActive = model.IsActive;
            a.Type = model.Type; a.IsPostable = model.IsPostable; a.ParentId = model.ParentId;
            TempData["StatusMessage"] = $"تم تحديث الحساب {a.Code} — {a.Name}.";
        }
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var a = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return RedirectToAction(nameof(Index));
        if (IsLocked(a)) { TempData["ErrorMessage"] = "لا يمكن حذف حساب رئيسي / مُدار من النظام."; return RedirectToAction(nameof(Index)); }
        if (await _db.Accounts.AnyAsync(x => x.ParentId == id, ct)) { TempData["ErrorMessage"] = "لا يمكن حذف حساب له حسابات فرعية."; return RedirectToAction(nameof(Index)); }
        if (await _db.JournalLines.AnyAsync(l => l.AccountId == id, ct)) { TempData["ErrorMessage"] = "لا يمكن حذف حساب عليه قيود."; return RedirectToAction(nameof(Index)); }
        _db.Accounts.Remove(a);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف الحساب {a.Code} — {a.Name}.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Persists the drag/drop tree: each node's new parent + order. One transaction.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string payload, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        List<AccountMove>? moves;
        try { moves = JsonSerializer.Deserialize<List<AccountMove>>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { return Json(new { ok = false, error = "بيانات غير صالحة." }); }
        if (moves is null || moves.Count == 0) return Json(new { ok = true });

        var accounts = await _db.Accounts.ToListAsync(ct);
        var byId = accounts.ToDictionary(a => a.Id);

        // Reject any move that would create a cycle (a node placed under itself or a descendant).
        bool CreatesCycle(Guid id, Guid? parentId)
        {
            var p = parentId;
            while (p is Guid pid)
            {
                if (pid == id) return true;
                p = byId.TryGetValue(pid, out var pa) ? pa.ParentId : null;
            }
            return false;
        }

        foreach (var m in moves)
        {
            if (!byId.TryGetValue(m.Id, out var a)) continue;
            var newParent = m.ParentId;
            if (newParent is Guid np && !byId.ContainsKey(np)) newParent = null;
            if (CreatesCycle(m.Id, newParent)) return Json(new { ok = false, error = "ترتيب غير صالح (تداخل حلقي)." });
            a.ParentId = newParent;
            a.SortOrder = m.SortOrder;
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم حفظ ترتيب دليل الحسابات.";
        return Json(new { ok = true });
    }

    private async Task<List<AccountOption>> ParentOptionsAsync(Guid? excludeId, CancellationToken ct) =>
        await _db.Accounts.Where(a => a.Id != excludeId && a.SubKind == null).OrderBy(a => a.Code)
            .Select(a => new AccountOption(a.Id, a.Code, a.Name, (int)a.Type)).ToListAsync(ct);
}
