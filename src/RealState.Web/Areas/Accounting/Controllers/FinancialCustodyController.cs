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

/// <summary>
/// العُهد المالية — cash custodies / imprest funds (FC-{year}{seq}). Cash is disbursed to an employee from a safe, then
/// settled with the accountant: approved expenses and cash returned to a safe bring the balance to zero and the custody
/// is closed. Ledger (the custodian's subsidiary of «العُهد المالية» 1350):
///   صرف للموظف   Dr العُهد المالية / Cr الخزنة       (سند صرف — TxnSource.CustodyDisbursement)
///   مصروف مُعتمد  Dr حساب البند / Cr العُهد المالية   (قيد — source CustodyExpense)
///   رد نقدية      Dr الخزنة / Cr العُهد المالية       (سند قبض — TxnSource.CustodyReturn)
/// A closed custody is read-only until reopened. Every change is recorded in the activity log.
/// </summary>
[Area("Accounting")]
[Authorize(Policy = PermissionNames.FinancialCustodyView)]
public class FinancialCustodyController : Controller
{
    private const long MaxFileBytes = 15 * 1024 * 1024;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccountingService _accounting;
    private readonly ISafeBalanceGuard _guard;

    public FinancialCustodyController(IApplicationDbContext db, ICurrentUserService currentUser, IAccountingService accounting, ISafeBalanceGuard guard)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
        _guard = guard;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    /// <summary>A refused request changed nothing — keep it out of the activity log.</summary>
    private void Refused() => HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;
    private static string FC(int number) => "FC-" + number;

    // ---------- List ----------
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? employeeId, FinancialCustodyStatus? status, string? q, CancellationToken ct)
    {
        ViewData["CanCreate"] = Can(PermissionNames.FinancialCustodyCreate);
        return View(await BuildListAsync(from, to, employeeId, status, q, ct));
    }

    [HttpGet]
    public async Task<IActionResult> PrintList(DateTime? from, DateTime? to, Guid? employeeId, FinancialCustodyStatus? status, string? q, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View(await BuildListAsync(from, to, employeeId, status, q, ct));
    }

    private async Task<FinancialCustodyListVm> BuildListAsync(DateTime? from, DateTime? to, Guid? employeeId, FinancialCustodyStatus? status, string? q, CancellationToken ct)
    {
        var empNames = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var custodies = await _db.FinancialCustodies.AsNoTracking().ToListAsync(ct);
        var sums = (await _db.FinancialCustodyMovements.GroupBy(m => new { m.FinancialCustodyId, m.Kind })
                .Select(g => new { g.Key.FinancialCustodyId, g.Key.Kind, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToLookup(x => x.FinancialCustodyId);
        decimal Sum(Guid id, CustodyMovementKind k) => sums[id].Where(x => x.Kind == k).Sum(x => x.Sum);

        var rows = custodies.Select(c => new FinancialCustodyRow
        {
            Id = c.Id, Number = c.Number, Date = c.Date, Employee = empNames.GetValueOrDefault(c.EmployeeId, "—"), Purpose = c.Purpose,
            Disbursed = Sum(c.Id, CustodyMovementKind.Disbursement), Spent = Sum(c.Id, CustodyMovementKind.Expense),
            Returned = Sum(c.Id, CustodyMovementKind.Return), Status = c.Status
        }).ToList();
        var empOf = custodies.ToDictionary(c => c.Id, c => c.EmployeeId);

        var filtered = rows.AsEnumerable();
        if (from.HasValue) filtered = filtered.Where(r => r.Date >= from.Value.Date);
        if (to.HasValue) filtered = filtered.Where(r => r.Date < to.Value.Date.AddDays(1));
        if (employeeId.HasValue) filtered = filtered.Where(r => empOf[r.Id] == employeeId);
        if (status.HasValue) filtered = filtered.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            filtered = filtered.Where(r => r.Purpose.Contains(term, StringComparison.OrdinalIgnoreCase)
                                           || r.Employee.Contains(term, StringComparison.OrdinalIgnoreCase)
                                           || FC(r.Number).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var open = rows.Where(r => r.Status == FinancialCustodyStatus.Open).ToList();
        return new FinancialCustodyListVm
        {
            Rows = filtered.OrderByDescending(r => r.Number).ToList(),
            From = from, To = to, EmployeeId = employeeId, Status = status, Q = q,
            Employees = await EmployeeOptionsAsync(activeOnly: false, ct),
            OpenCount = open.Count,
            HeldByEmployees = open.Where(r => r.Balance > 0).Sum(r => r.Balance),
            OwedToEmployees = -open.Where(r => r.Balance < 0).Sum(r => r.Balance),
        };
    }

    // ---------- Details / statement ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var vm = await DetailsAsync(id, ct);
        if (vm is null) return NotFound();
        ViewData["CanCreate"] = Can(PermissionNames.FinancialCustodyCreate);
        ViewData["CanSettle"] = Can(PermissionNames.FinancialCustodySettle);
        ViewData["CanDelete"] = Can(PermissionNames.FinancialCustodyDelete);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Print(Guid id, CancellationToken ct)
    {
        var vm = await DetailsAsync(id, ct);
        if (vm is null) return NotFound();
        ViewBag.TenantId = _currentUser.TenantId;
        return View(vm);
    }

    private async Task<FinancialCustodyDetailsVm?> DetailsAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.FinancialCustodies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        var emp = await _db.Employees.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == c.EmployeeId, ct);
        var moves = await _db.FinancialCustodyMovements.Where(m => m.FinancialCustodyId == id)
            .Select(m => new { m.Id, m.Kind, m.Date, m.Amount, m.Description, m.SafeId, m.SafeTransactionId, m.CategoryId, m.ProjectId, m.ReceiptFileName, m.CreatedAt })
            .ToListAsync(ct);
        var safeNames = await _db.Safes.IgnoreQueryFilters().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var catNames = await _db.TxnCategories.ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var txnIds = moves.Where(m => m.SafeTransactionId != null).Select(m => m.SafeTransactionId!.Value).ToList();
        var serials = await _db.SafeTransactions.Where(t => txnIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Serial, ct);

        // Chronological, disbursements first on the same day, with the custodian's running balance.
        decimal running = 0;
        var rows = new List<FinancialCustodyMovementRow>();
        foreach (var m in moves.OrderBy(m => m.Date).ThenBy(m => m.Kind == CustodyMovementKind.Disbursement ? 0 : 1).ThenBy(m => m.CreatedAt))
        {
            running += m.Kind == CustodyMovementKind.Disbursement ? m.Amount : -m.Amount;
            rows.Add(new FinancialCustodyMovementRow
            {
                Id = m.Id, Kind = m.Kind, Date = m.Date, Amount = m.Amount, Description = m.Description,
                Safe = m.SafeId is Guid s ? safeNames.GetValueOrDefault(s) : null,
                Category = m.Kind == CustodyMovementKind.Expense ? (m.CategoryId is Guid cat ? catNames.GetValueOrDefault(cat, "عام") : "عام") : null,
                Project = m.ProjectId is Guid p ? projNames.GetValueOrDefault(p) : null,
                VoucherId = m.SafeTransactionId,
                VoucherSerial = m.SafeTransactionId is Guid t && serials.TryGetValue(t, out var sn) ? sn : null,
                ReceiptFileName = m.ReceiptFileName, Balance = running
            });
        }

        return new FinancialCustodyDetailsVm
        {
            Custody = c, Employee = emp,
            Department = emp?.DepartmentId is Guid d ? await _db.Departments.Where(x => x.Id == d).Select(x => x.Name).FirstOrDefaultAsync(ct) : null,
            Movements = rows,
            Disbursed = rows.Where(r => r.Kind == CustodyMovementKind.Disbursement).Sum(r => r.Amount),
            Spent = rows.Where(r => r.Kind == CustodyMovementKind.Expense).Sum(r => r.Amount),
            Returned = rows.Where(r => r.Kind == CustodyMovementKind.Return).Sum(r => r.Amount),
        };
    }

    // ---------- Open a custody (with its first disbursement) ----------
    [HttpGet]
    public async Task<IActionResult> Form(CancellationToken ct)
    {
        if (!Can(PermissionNames.FinancialCustodyCreate)) return Forbid();
        return PartialView("_FinancialCustodyForm", await FillAsync(new FinancialCustodyCreateModel(), ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(FinancialCustodyCreateModel model, CancellationToken ct)
    {
        if (!Can(PermissionNames.FinancialCustodyCreate)) return Forbid();
        if (ModelState.IsValid && await _guard.CheckWithdrawalAsync(model.SafeId!.Value, model.Amount, ct) is string overdraw)
            ModelState.AddModelError(nameof(model.SafeId), overdraw);
        if (!ModelState.IsValid) return PartialView("_FinancialCustodyForm", await FillAsync(model, ct));

        long lo = YearSerial.Base(model.Date.Year, 4), hi = YearSerial.End(model.Date.Year, 4);
        var max = await _db.FinancialCustodies.Where(x => x.Number > lo && x.Number < hi).MaxAsync(x => (int?)x.Number, ct);
        var custody = new FinancialCustody
        {
            Number = (int)YearSerial.Next(max, model.Date.Year, 4), Date = model.Date, EmployeeId = model.EmployeeId!.Value,
            Purpose = model.Purpose.Trim(), Notes = model.Notes, Status = FinancialCustodyStatus.Open
        };
        _db.FinancialCustodies.Add(custody);
        var empName = await EmployeeNameAsync(custody.EmployeeId, ct);
        await AddCashAsync(custody, empName, CustodyMovementKind.Disbursement, model.Date, model.Amount, model.SafeId!.Value, custody.Purpose, ct);
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم فتح العهدة المالية {FC(custody.Number)} وصرف {model.Amount:N2} ج.م إلى {empName}.";
        return Json(new { ok = true, redirect = Url.Action(nameof(Details), new { id = custody.Id }) });
    }

    [HttpGet]
    public async Task<IActionResult> EditForm(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.FinancialCustodyCreate)) return Forbid();
        var c = await _db.FinancialCustodies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        return PartialView("_FinancialCustodyEditForm", new FinancialCustodyEditModel { Id = c.Id, Number = c.Number, Purpose = c.Purpose, Notes = c.Notes });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(FinancialCustodyEditModel model, CancellationToken ct)
    {
        if (!Can(PermissionNames.FinancialCustodyCreate)) return Forbid();
        var c = await _db.FinancialCustodies.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
        if (c is null) return NotFound();
        if (!ModelState.IsValid) { model.Number = c.Number; return PartialView("_FinancialCustodyEditForm", model); }
        c.Purpose = model.Purpose.Trim();
        c.Notes = model.Notes;
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم تعديل بيانات العهدة المالية {FC(c.Number)}.";
        return Json(new { ok = true });
    }

    // ---------- Cash through a safe: extra disbursement / cash returned by the custodian ----------
    [HttpGet]
    public async Task<IActionResult> CashForm(Guid id, CustodyMovementKind kind, CancellationToken ct)
    {
        var c = await _db.FinancialCustodies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (!CanCash(kind)) return Forbid();
        if (c.Status != FinancialCustodyStatus.Open) return ClosedMessage(c);
        var model = new CustodyCashModel { CustodyId = c.Id, Number = c.Number, Kind = kind, Balance = await BalanceAsync(c.Id, ct) };
        if (kind == CustodyMovementKind.Return && model.Balance > 0) model.Amount = model.Balance;
        return PartialView("_CustodyCashForm", await FillAsync(model, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cash(CustodyCashModel model, CancellationToken ct)
    {
        var c = await _db.FinancialCustodies.FirstOrDefaultAsync(x => x.Id == model.CustodyId, ct);
        if (c is null) return NotFound();
        if (!CanCash(model.Kind) || model.Kind == CustodyMovementKind.Expense) return Forbid();
        if (c.Status != FinancialCustodyStatus.Open) { Refused(); return Json(new { ok = false, error = "العهدة مُقفلة — أعد فتحها أولًا." }); }

        var balance = await BalanceAsync(c.Id, ct);
        if (ModelState.IsValid && model.Kind == CustodyMovementKind.Return && model.Amount > balance)
            ModelState.AddModelError(nameof(model.Amount), $"المبلغ أكبر من الرصيد لدى الموظف ({balance:N2} ج.م).");
        if (ModelState.IsValid && model.Kind == CustodyMovementKind.Disbursement
            && await _guard.CheckWithdrawalAsync(model.SafeId!.Value, model.Amount, ct) is string overdraw)
            ModelState.AddModelError(nameof(model.SafeId), overdraw);
        if (!ModelState.IsValid)
        {
            model.Number = c.Number; model.Balance = balance;
            return PartialView("_CustodyCashForm", await FillAsync(model, ct));
        }

        var empName = await EmployeeNameAsync(c.EmployeeId, ct);
        await AddCashAsync(c, empName, model.Kind, model.Date, model.Amount, model.SafeId!.Value, model.Description, ct);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = model.Kind == CustodyMovementKind.Disbursement
            ? $"تم صرف {model.Amount:N2} ج.م إضافية على العهدة المالية {FC(c.Number)}."
            : $"تم استلام {model.Amount:N2} ج.م نقدًا من العهدة المالية {FC(c.Number)} إلى الخزنة.";
        return Json(new { ok = true });
    }

    private bool CanCash(CustodyMovementKind kind) => kind == CustodyMovementKind.Disbursement
        ? Can(PermissionNames.FinancialCustodyCreate)
        : Can(PermissionNames.FinancialCustodySettle);

    // ---------- Settlement: approve the expenses paid from the custody ----------
    [HttpGet]
    public async Task<IActionResult> SettleForm(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.FinancialCustodySettle)) return Forbid();
        var c = await _db.FinancialCustodies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (c.Status != FinancialCustodyStatus.Open) return ClosedMessage(c);
        var model = new CustodySettleModel { CustodyId = c.Id, Number = c.Number, Balance = await BalanceAsync(c.Id, ct) };
        model.Lines.Add(new CustodyExpenseLine { Date = ClientTime.Today });
        return PartialView("_CustodySettleForm", await FillAsync(model, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settle(CustodySettleModel model, CancellationToken ct)
    {
        if (!Can(PermissionNames.FinancialCustodySettle)) return Forbid();
        var c = await _db.FinancialCustodies.FirstOrDefaultAsync(x => x.Id == model.CustodyId, ct);
        if (c is null) return NotFound();
        if (c.Status != FinancialCustodyStatus.Open) { Refused(); return Json(new { ok = false, error = "العهدة مُقفلة — أعد فتحها أولًا." }); }

        // Fully empty lines are ignored; a started line needs a date, a description and a positive amount.
        var lines = model.Lines.Select((l, i) => (Line: l, No: i + 1))
            .Where(x => x.Line.Amount is not null || !string.IsNullOrWhiteSpace(x.Line.Description) || x.Line.Receipt is not null)
            .ToList();
        if (lines.Count == 0) ModelState.AddModelError("Lines", "أضف مصروفًا واحدًا على الأقل.");
        foreach (var (l, no) in lines)
        {
            if (l.Date is null || string.IsNullOrWhiteSpace(l.Description) || l.Amount is null || l.Amount <= 0)
                ModelState.AddModelError("Lines", $"السطر {no}: أدخل التاريخ والبيان ومبلغًا أكبر من صفر.");
            if (l.Receipt is { Length: > MaxFileBytes })
                ModelState.AddModelError("Lines", $"السطر {no}: الحد الأقصى لحجم الإيصال {MaxFileBytes / (1024 * 1024)} ميجابايت.");
        }
        if (!ModelState.IsValid)
        {
            model.Number = c.Number; model.Balance = await BalanceAsync(c.Id, ct);
            foreach (var l in model.Lines) l.Receipt = null;   // files can't be re-shown; the user picks them again
            return PartialView("_CustodySettleForm", await FillAsync(model, ct));
        }

        var empName = await EmployeeNameAsync(c.EmployeeId, ct);
        decimal total = 0;
        foreach (var (l, _) in lines)
        {
            var m = new FinancialCustodyMovement
            {
                FinancialCustodyId = c.Id, Kind = CustodyMovementKind.Expense, Date = l.Date!.Value, Amount = Math.Round(l.Amount!.Value, 2),
                Description = l.Description!.Trim(), CategoryId = l.CategoryId, ProjectId = l.ProjectId
            };
            if (l.Receipt is { Length: > 0 } file)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                m.ReceiptFileName = Path.GetFileName(file.FileName);
                m.ReceiptContentType = file.ContentType;
                m.ReceiptData = ms.ToArray();
            }
            _db.FinancialCustodyMovements.Add(m);
            await _accounting.PostCustodyExpenseAsync(m, c.EmployeeId, $"مصروف من العهدة المالية {FC(c.Number)} ({empName}) — {m.Description}", ct);
            total += m.Amount;
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم اعتماد {lines.Count} مصروف بإجمالي {total:N2} ج.م على العهدة المالية {FC(c.Number)}.";
        return Json(new { ok = true });
    }

    [HttpGet]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken ct)
    {
        var m = await _db.FinancialCustodyMovements.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m?.ReceiptData is null) return NotFound();
        return File(m.ReceiptData, m.ReceiptContentType ?? "application/octet-stream", m.ReceiptFileName ?? "receipt");
    }

    // ---------- Close / reopen ----------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.FinancialCustodySettle)) return Forbid();
        var c = await _db.FinancialCustodies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        var balance = await BalanceAsync(c.Id, ct);
        if (balance != 0)
        {
            Refused(); TempData["ErrorMessage"] = balance > 0
                ? $"لا يمكن إقفال العهدة {FC(c.Number)}: لدى الموظف {balance:N2} ج.م — سجّل المصروفات أو استلم النقدية أولًا."
                : $"لا يمكن إقفال العهدة {FC(c.Number)}: للموظف {-balance:N2} ج.م (مصروفات زائدة) — اصرفها له أولًا.";
            return RedirectToAction(nameof(Details), new { id });
        }
        c.Status = FinancialCustodyStatus.Closed;
        c.ClosedAt = ClientTime.Now;
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تمت تسوية وإقفال العهدة المالية {FC(c.Number)}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.FinancialCustodySettle)) return Forbid();
        var c = await _db.FinancialCustodies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        c.Status = FinancialCustodyStatus.Open;
        c.ClosedAt = null;
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تمت إعادة فتح العهدة المالية {FC(c.Number)}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------- Reverse a movement / delete a custody ----------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMovement(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.FinancialCustodyDelete)) return Forbid();
        var m = await _db.FinancialCustodyMovements.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return NotFound();
        var c = await _db.FinancialCustodies.FirstAsync(x => x.Id == m.FinancialCustodyId, ct);
        if (c.Status != FinancialCustodyStatus.Open)
        {
            Refused(); TempData["ErrorMessage"] = $"العهدة {FC(c.Number)} مُقفلة — أعد فتحها أولًا.";
            return RedirectToAction(nameof(Details), new { id = c.Id });
        }
        if (await ReverseAsync(m, ct) is string error)
        {
            Refused(); TempData["ErrorMessage"] = error;
            return RedirectToAction(nameof(Details), new { id = c.Id });
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف حركة «{m.Kind.Ar()}» بمبلغ {m.Amount:N2} ج.م من العهدة المالية {FC(c.Number)} وعكس قيدها.";
        return RedirectToAction(nameof(Details), new { id = c.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.FinancialCustodyDelete)) return Forbid();
        var c = await _db.FinancialCustodies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (c.Status != FinancialCustodyStatus.Open)
        {
            Refused(); TempData["ErrorMessage"] = $"العهدة {FC(c.Number)} مُقفلة — أعد فتحها أولًا.";
            return RedirectToAction(nameof(Details), new { id });
        }
        foreach (var m in await _db.FinancialCustodyMovements.Where(x => x.FinancialCustodyId == id).ToListAsync(ct))
        {
            if (await ReverseAsync(m, ct) is string error)
            {
                Refused(); TempData["ErrorMessage"] = error;
                return RedirectToAction(nameof(Details), new { id });
            }
        }
        _db.FinancialCustodies.Remove(c);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف العهدة المالية {FC(c.Number)} وعكس كل قيودها وسنداتها.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Removes a movement with its voucher / journal entry. Null on success, else an Arabic reason.</summary>
    private async Task<string?> ReverseAsync(FinancialCustodyMovement m, CancellationToken ct)
    {
        if (m.SafeTransactionId is Guid txnId && await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == txnId, ct) is { } txn)
        {
            // Removing a cash return takes that money back out of the safe — «سحب على المكشوف» applies.
            if (m.Kind == CustodyMovementKind.Return && await _guard.CheckWithdrawalAsync(txn.SafeId, txn.Amount, ct) is string overdraw)
                return $"لا يمكن عكس رد النقدية ({m.Amount:N2} ج.م): {overdraw}";
            await _accounting.RemoveTransactionAsync(txn, ct);
        }
        if (m.Kind == CustodyMovementKind.Expense)
            await _accounting.RemoveObligationAsync(AccountingSources.CustodyExpense, m.Id, ct);
        _db.FinancialCustodyMovements.Remove(m);
        return null;
    }

    // ---------- helpers ----------
    /// <summary>Records a disbursement / cash return: its cash voucher (posting Dr/Cr «العُهد المالية» against the safe) + the movement.</summary>
    private async Task AddCashAsync(FinancialCustody c, string? empName, CustodyMovementKind kind, DateTime date, decimal amount, Guid safeId,
        string? description, CancellationToken ct)
    {
        var text = (kind == CustodyMovementKind.Disbursement ? "صرف عهدة مالية" : "رد نقدية من عهدة مالية")
                   + $" {FC(c.Number)} — {empName}" + (string.IsNullOrWhiteSpace(description) ? "" : $" — {description.Trim()}");
        var txn = await _accounting.AddTransactionAsync(safeId,
            kind == CustodyMovementKind.Disbursement ? TxnType.Expense : TxnType.Income,
            kind == CustodyMovementKind.Disbursement ? TxnSource.CustodyDisbursement : TxnSource.CustodyReturn,
            amount, date, text, employeeId: c.EmployeeId, ct: ct);
        _db.FinancialCustodyMovements.Add(new FinancialCustodyMovement
        {
            FinancialCustodyId = c.Id, Kind = kind, Date = date, Amount = amount, SafeId = safeId, SafeTransactionId = txn.Id,
            Description = string.IsNullOrWhiteSpace(description) ? (kind == CustodyMovementKind.Disbursement ? "صرف للموظف" : "رد النقدية المتبقية") : description.Trim()
        });
    }

    /// <summary>What the custodian holds: Σ disbursed − Σ approved expenses − Σ returned (negative = owed to them).</summary>
    private async Task<decimal> BalanceAsync(Guid custodyId, CancellationToken ct) =>
        await _db.FinancialCustodyMovements.Where(m => m.FinancialCustodyId == custodyId)
            .SumAsync(m => m.Kind == CustodyMovementKind.Disbursement ? m.Amount : -m.Amount, ct);

    private async Task<string?> EmployeeNameAsync(Guid id, CancellationToken ct) =>
        await _db.Employees.IgnoreQueryFilters().Where(e => e.Id == id).Select(e => e.FullName).FirstOrDefaultAsync(ct);

    private ContentResult ClosedMessage(FinancialCustody c) =>
        Content($"<div style=\"padding:18px;color:var(--warning);text-align:center;\">العهدة {FC(c.Number)} مُقفلة — أعد فتحها أولًا.</div>", "text/html");

    private async Task<FinancialCustodyCreateModel> FillAsync(FinancialCustodyCreateModel m, CancellationToken ct)
    {
        m.Employees = await EmployeeOptionsAsync(activeOnly: true, ct);
        m.Safes = await SafeOptionsAsync(ct);
        return m;
    }

    private async Task<CustodyCashModel> FillAsync(CustodyCashModel m, CancellationToken ct)
    {
        m.Safes = await SafeOptionsAsync(ct);
        return m;
    }

    private async Task<CustodySettleModel> FillAsync(CustodySettleModel m, CancellationToken ct)
    {
        // User-defined expense categories post to their own account; none = مصروفات عامة.
        m.Categories = (await _db.TxnCategories.Where(x => x.Type == TxnType.Expense && x.IsActive && !x.IsBuiltIn)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct))
            .Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToList();
        m.Projects = (await _db.Projects.OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync(ct))
            .Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToList();
        return m;
    }

    /// <summary>Active safes with their current balance in the label.</summary>
    private async Task<List<SelectListItem>> SafeOptionsAsync(CancellationToken ct)
    {
        var safes = await _db.Safes.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var list = new List<SelectListItem>();
        foreach (var s in safes)
            list.Add(new SelectListItem($"{s.Name} — الرصيد {await _guard.BalanceAsync(s.Id, ct):N2}", s.Id.ToString()));
        return list;
    }

    private async Task<List<SelectListItem>> EmployeeOptionsAsync(bool activeOnly, CancellationToken ct) =>
        (await _db.Employees.Where(e => !activeOnly || e.IsActive).OrderBy(e => e.FullName).Select(e => new { e.Id, e.FullName }).ToListAsync(ct))
        .Select(e => new SelectListItem(e.FullName, e.Id.ToString())).ToList();
}
