using Microsoft.AspNetCore.Mvc;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Web.Areas.Accounting.Controllers;

[Area("Accounting")]
public class ExpensesController : TxnControllerBase
{
    public ExpensesController(IApplicationDbContext db, IAccountingService accounting, ICurrentUserService currentUser)
        : base(db, accounting, currentUser) { }

    protected override TxnType TxnType => TxnType.Expense;
    protected override string ViewPerm => PermissionNames.ExpensesView;
    protected override string CreatePerm => PermissionNames.ExpensesCreate;
    protected override string EditPerm => PermissionNames.ExpensesEdit;
    protected override string DeletePerm => PermissionNames.ExpensesDelete;
}
