using Microsoft.AspNetCore.Mvc;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Web.Areas.Accounting.Controllers;

[Area("Accounting")]
public class IncomesController : TxnControllerBase
{
    public IncomesController(IApplicationDbContext db, IAccountingService accounting, ICurrentUserService currentUser)
        : base(db, accounting, currentUser) { }

    protected override TxnType TxnType => TxnType.Income;
    protected override string ViewPerm => PermissionNames.IncomesView;
    protected override string CreatePerm => PermissionNames.IncomesCreate;
    protected override string EditPerm => PermissionNames.IncomesEdit;
    protected override string DeletePerm => PermissionNames.IncomesDelete;
}
