# Real Estate Business Management System — Features

An Arabic (RTL), dark‑themed, multi‑tenant business‑operations platform for real‑estate developers:
projects & construction stages, unit sales with installment collection, CRM, suppliers/contractors
with purchase orders, a full cash/accounting ledger, marketing campaigns, reporting, and
role/permission‑based administration.

---

## 1. Technology & Architecture

- **.NET 9 / ASP.NET Core MVC** (Razor views, Areas), C#.
- **Clean architecture**, 5 projects:
  - `RealState.Domain` / `RealState.Application` — entities, enums, interfaces, services (Dashboard, Accounting), DTOs.
  - `RealState.Infrastructure` — EF Core `ApplicationDbContext`, ASP.NET Identity, seeding, migrations, Serilog.
  - `RealState.Shared` — cross‑cutting (Result, `PermissionNames`, constants).
  - `RealState.Web` — MVC UI, Areas, controllers, views.
- **EF Core 9 + SQL Server**; code‑first migrations (auto‑applied on startup in Development).
- **ASP.NET Identity** (Guid keys) for users/roles.
- **Serilog** (console + rolling file).
- **Charts** via vendored **Chart.js** (CSP‑safe, no CDN).
- **Excel** export capability present (ClosedXML); **FluentValidation** available.
- UI: **Arabic RTL**, dark theme, self‑hosted fonts, right‑hand collapsible sidebar; reusable
  **modal CRUD** (`app-modal.js`) and system‑wide **SweetAlert** confirmations/toasts/errors.

### Cross‑cutting platform capabilities
- **Multi‑tenant**: every business entity is tenant‑scoped via EF **global query filters**
  (`TenantId == current`), stamped automatically on save. A super‑admin “host” user (`syncro`)
  can create/switch tenants.
- **Soft‑delete**: deletes are converted to `IsDeleted` flags and filtered out globally.
- **Auditing**: `CreatedAt/By`, `UpdatedAt/By`, `DeletedAt/By`, `RowVersion` concurrency token on
  every auditable entity.
- **Granular permissions**: ~47 permissions, each = one authorization policy + one assignable
  privilege. Per‑user permission claims; SuperAdmin/TenantAdmin roles seeded with full sets.
- **Activity log**: authenticated POST actions and logins are recorded (who/when/what) with a
  viewer screen.
- **Global exception handling**: AJAX → `{ok:false,error}` (SweetAlert); navigation → error banner.
- **Printing**: virtually every list/record has a branded, auto‑printing **PDF view** (tenant logo,
  RTL) that opens in a new tab.
- **Date filters** default to “today” on a fresh page open, respected on explicit submit.

---

## 2. Modules & Features

### 2.1 Executive Dashboard (`/`)
- KPI cards: **المشاريع, مبيعات الشهر, مديونيات العملاء, مستحقات الموردين, إجمالي التحصيل**.
- Charts: collections donut, projects sell‑through bars; recent sales table; current‑stage &
  delay indicators.

### 2.2 Projects & Construction (`Projects` area)
- **Projects** CRUD (Building / Mall / Land types), hero image, planned vs actual dates, location,
  notes; project cards + list with sell‑through stats.
- **Units** (for Building/Mall): name, number, area (m²), price, status (NotReady/Available/Sold);
  price pre‑fills the sale total.
- **Stages** managed **inline** under the project’s المراحل tab: add/edit/delete, planned/actual
  dates, and **status** badge (لم تبدأ / قيد التنفيذ / مكتملة).
  - **Start / End** actions ask for the actual date, update actual start/end, and **log an activity**.
  - Per‑stage **activity log**; delay flags when actual > planned.
  - Reusable **stage definitions** master list (under Settings).
- **Project expenses** tab (المصاريف): manual project expenses + auto expenses from supplier‑order
  payments charged to the project; shows source and total.
- **Attachments** (images/PDF/Word/Excel/text, stored in DB) with preview/download.
- **Prints**: project summary, units list, attachments list, all‑projects summary, stage sheet.

### 2.3 Sales — Contracts (`Sales` area)
- **Sale contracts**: customer, project→unit (cascading, project‑filtered), contract date, receive
  date, total price, down payment, installment count & period, notes.
- **Down payment is scheduled as installment #0** (labeled المقدم) and collected via Collections —
  not counted as paid up front; **first‑installment date** is configurable.
- Contracts list with text search + date‑range filter; the selected filter is preserved when
  drilling into a contract and back.
- **Sales summary** landing page: KPI cards (with MoM deltas), monthly‑sales line chart, sales‑by‑
  project bar chart, latest contracts — plus a branded print.
- **Prints**: contract document (parties, unit details, installment schedule), contracts list PDF.

### 2.4 Collections — تحصيلات المشاريع (`Sales` area)
- Outstanding/overdue KPIs; due‑now, due‑this‑week, all, and collected tabs; grouped & searchable.
- **Collect an installment** (choose safe) → records an **income** movement and issues a printable
  **إيصال سداد/تحصيل** (receipt no. `C‑#####`). **Cancel** a collection reverses the income.
- Batch “print all” collections report.

### 2.4.1 Product sales invoices — فواتير المبيعات (`Sales` area)
Selling inventory **products** to a customer — separate from real‑estate contracts, but billed to the same
**Customers** (leads excluded). Mirrors purchase invoices. Menu: المبيعات ← فواتير المبيعات (الأصناف).
- Number `SI‑YYYY######` (shown on the form before saving); date (not future); searchable customer
  (required), warehouse (no project); product lines (searchable by code/name) with unit, **selling
  price entered manually**, quantity, total. The stock column («المتاح بالمخزن») shows the product's
  balance **in the chosen warehouse** and flags ⚠ a quantity above it (the save is refused too).
- **Journal entries** (القيود المحاسبية):
  | Entry | Debit | Credit |
  |---|---|---|
  | Invoice (selling price) | العملاء — customer subsidiary | إيرادات مبيعات البضائع (4200) |
  | Automatic goods issue (cost) | تكلفة المبيعات | مخزون — warehouse subsidiary |
  | Collection | الخزنة | العملاء — customer subsidiary |
  The revenue account is the inventory posting profile's «حساب إيرادات المبيعات» — now **4200 إيرادات
  مبيعات البضائع** (created per tenant; profiles still on the reserved, never‑used 4100 were repointed by
  migration `ProductSalesInvoices`), so product sales stay apart from real‑estate sales (4100).
- **Stock**: stock‑tracked lines leave the warehouse through an automatic, posted **goods issue (reason:
  sale)** costed by the costing method; non‑stock products are revenue only. Edits that change
  lines/quantities/warehouse/date reverse the issue (kept as معكوس, stock returns) and post a new one;
  price/customer/notes edits only re‑post the invoice entry. Auto issues are managed only from their
  invoice (the goods‑issues page shows «من الفاتورة SI‑…»). The inventory engine's stock check now also
  counts movements pending in the same save, so reverse‑then‑reissue is one atomic save.
- **Details page**: total / collected / remaining / **gross profit** (total − cost of sales, with
  margin), lines, the goods issue(s), collections. Prints: invoice PDF, filtered list PDF.
- **Collections (تحصيل)** per invoice into a safe → income movement (source «تحصيل فاتورة مبيعات»),
  printable **إيصال استلام نقدية**; **cancel** a collection (overdraft rule applies).
- Guards: can't delete an invoice with collections; total can't drop below collected; customer can't
  change once collected; a customer / product used by sales invoices can't be deleted.
- Customer page: new **فواتير المبيعات** tab (totals + list + «➕ فاتورة مبيعات»). Startup‑page option.
- **Sales summary** (the «المبيعات» menu parent, `SalesInvoices/Summary`): KPIs with month‑over‑month
  deltas (sales, collected, invoices, new buying customers), gross profit + margin, outstanding balance
  (customers with a balance), monthly sales chart (last 6 months), top buyers chart, latest invoices,
  top customer balances — plus a branded print. Sales and collections are net of sales returns.

### 2.4.2 Returns — مرتجعات المبيعات / مرتجعات المشتريات (`SalesReturns`, `Suppliers/PurchaseReturns`)
Goods coming back from a customer (sales return, `SR‑YYYY######`) or going back to a supplier (purchase return,
`PR‑YYYY######`) — always **against one invoice**, line by line, never more of a line than invoiced minus earlier
returns (the form shows invoiced / returned before / returnable per line). One shared form, list, details and
print (`Views/Shared/Returns`, `wwwroot/js/return-doc.js`); opened from the returns page («↩ مرتجع … جديد», with an
invoice picker) or from an invoice («↩ مرتجع»). A return isn't edited: delete it (stock, entries and cash are
reversed) and record it again.
- **Credit / debit note**: an invoice now leaves `total − returns − (paid − refunds)` owed (`InvoiceReturns`) —
  everywhere: invoice pages and lists, customer page, supplier statement (return −, refund + rows), supplier pay
  form, suppliers report, dashboard payables, workspace KPIs, sales summary (cost of sales net of returned cost).
- **Cash refund** (optional): «رد نقدية للعميل» from a safe (overdraft rule applies) / «استرداد نقدية من المورد» into a
  safe. It's **required** for the part of the return the invoice no longer owes (value − remaining) and capped at
  the return's value and at what was paid and not refunded yet — the form keeps it within these limits live.
  Printable voucher (إيصال صرف / استلام نقدية) — the unified cash voucher of the refund movement.
- **Journal entries**:
  | Entry | Debit | Credit |
  |---|---|---|
  | Sales return | مردودات المبيعات (4250) | العملاء — customer subsidiary |
  | Its automatic goods receipt (reason: sales return, at the original cost of sale) | مخزون — warehouse | تكلفة المبيعات |
  | Refund to the customer (source «رد نقدية مرتجع مبيعات») | العملاء | الخزنة |
  | Purchase return | الموردون — supplier subsidiary | بضاعة واردة لم تُفوتر (stock lines, by the issue cost) / مردودات المشتريات (5350, non‑stock lines) |
  | Its automatic goods issue (reason: purchase return, at the invoice cost) | بضاعة واردة لم تُفوتر | مخزون — warehouse |
  | Refund from the supplier (source «استرداد نقدية مرتجع مشتريات») | الخزنة | الموردون |
  GRNI nets to zero; if the stock on hand was worth less than the invoice cost, the difference goes to إيرادات
  أخرى / مصروفات عامة. 4250 / 5350 are created for existing tenants on first use.
- **Guards**: an invoice with returns can't be edited or deleted (delete the returns first); a collection can't be
  cancelled below what was refunded; deleting a sales return is refused if its stock was sold since; deleting a
  purchase return with a refund applies the overdraft rule. The returns' goods receipts / issues are managed
  only from the return (inventory pages show «↩ من المرتجع SR‑/PR‑…»).
- Permissions `SalesReturns.View/Create/Delete`, `PurchaseReturns.View/Create/Delete` — granted on upgrade to every
  user / role holding the matching invoice permission. Navigation: pages + «＋ إجراء جديد» actions; old
  `…/Returns` links redirect.

**Side menu (sales & purchasing):** التعاقدات (parent → contracts summary; العقود، التحصيلات) · العملاء
(المناديب، العملاء، تقرير العملاء) · المبيعات (parent → sales summary; فواتير المبيعات، مرتجعات المبيعات)
· الموردين (الموردين، تقرير الموردين) · المشتريات (أوامر التوريد، فواتير المشتريات، مرتجعات المشتريات). The customers / suppliers reports moved out of التقارير into their groups.
Placeholders use the shared `Views/Shared/ComingSoon.cshtml`.

### 2.5 CRM (`CRM` area)
- **Customers**: CRUD, unique phone, source, assigned salesperson; **statement (كشف حساب)** of all
  contracts with paid/remaining, printable statement, receipts, and due‑payment notices.
- **Salespersons (المناديب)** and **Leads** (marketing pipeline with statuses New→Won/Lost).

### 2.6 Suppliers & Contractors — الموردون والمقاولون (`Suppliers` area)
- **Suppliers** list/CRUD (name, phone, email, notes) + supplier **account statement (كشف حساب)**.
Purchasing runs in two stages — **purchase order → purchase invoice**.
- **Purchase orders (أوامر التوريد)** — stage 1, a plain request: number `PO‑YYYY####` (shown on the
  form before saving), date, optional project (searchable), product lines (الأصناف: product picked
  by code/name + its **unit (الوحدة)** + **quantity only** — no unit cost or totals). **No supplier**, and saving
  posts nothing (no ledger, stock or safe effect). The list shows each order’s **related invoices**;
  an order with invoices can’t be deleted. Orders from before the invoice stage keep their supplier,
  payable entry, payments and unit costs (shown on the order and in the supplier statement). When an
  invoice copies an order's lines, the unit costs are left empty to be entered from the supplier invoice.
- **Purchase invoices (فواتير المشتريات)** — stage 2, own menu item: number `PI‑YYYY######`
  (e.g. `PI‑2026000001`, shown on the form before saving), date, supplier (required), optional project
  and optional purchase order (all searchable; picking an order offers to copy its lines + project).
  Product lines show product, unit cost (entered manually), **current stock** (all warehouses), quantity,
  total, and each line's **unit (الوحدة)** from the product; plus a **warehouse** (required when there are
  stock‑tracked products). Several invoices may reference the same order, but **together they may not bill
  more of any product than the order has** (and only its products); copying an order brings its remaining
  quantities, the order page shows ordered / invoiced / remaining per product, and an order can't be cut
  below what was invoiced. Saving:
  - posts the invoice entry **Dr بضاعة واردة لم تُفوتر** (stock‑tracked products) / **Dr المشتريات**
    (non‑stock products), **Cr الموردون** (the supplier’s payable);
  - **auto‑creates and posts a goods receipt (إذن استلام)** for the stock‑tracked lines in the chosen
    warehouse at the invoice unit costs and date (Dr المخزون / Cr بضاعة واردة لم تُفوتر), so stock and
    the product’s cost follow the invoice under the costing method — net effect Dr المخزون / Cr المورد.
  Edits re‑post the entry and, when the stock lines/warehouse/date change, reverse the receipt and
  post a new one; deleting reverses both (blocked once paid, or when the received goods were already
  issued). Auto receipts are locked on the goods‑receipts page. Every goods receipt can be printed (🖨 in
  the list and in its details popup: header, supplier, source invoice, lines). ❓ help on the list, form and details
  explains the flow. List filters: date, supplier, project, order.
- **Per‑invoice payments**: pay from the invoice page or the statement’s **invoice picker**, capped at
  the invoice’s remaining. Each payment records a safe **expense** (on the invoice’s project), issues a
  printable **إيصال صرف نقدية** (opens in a new tab) and shows on the supplier’s statement.
- **Account statement** = a running‑balance ledger (المصدر / التاريخ / البيان / رصيد قبل / المبلغ /
  الرصيد) over invoices (+ legacy orders) and payments, with date‑range filter and period
  **closing balance**; editing an invoice below its paid amount is blocked.

### 2.7 Accounting / Finance (`Accounting` area)
- **Safes (الخزائن)**: CRUD, initial amount, active flag, **safe type** (عادية — default / بنك /
  محفظة إلكترونية / إنستاباي) with a type filter + column on the list; **movements** view with
  per‑transaction **running balance** and print.
- **Transfers between safes (التحويلات بين الخزائن)**: own page + menu item; number `SF‑YYYY######`
  (e.g. `SF‑2026000001`, shown on the form before saving), date/time, from/to safe (searchable, with
  balances), amount, notes; create/edit/delete (permission `Safes.Transfer`), printable **إذن تحويل
  نقدية**, list filters (date range + presets, one safe filter matching either side, text) with print +
  Excel. A transfer writes an outflow on the source safe and an inflow on the destination (source
  `SafeTransfer`, no income/expense serial) — so all safe balances include it — and posts **one direct
  journal entry: Dr destination safe / Cr source safe** (tagged with the transfer). Changes that would
  overdraw a safe are refused unless the safe has **سحب على المكشوف** enabled (a per‑safe option only users
  with `Safes.Overdraft` can change). The same rule guards every money‑out action: manual expenses and
  their edits, advance / reward payouts, project expenses, supplier and contractor payments, and removing an
  income (deleting an income, cancelling a collection, deleting a project with incomes). Transfers are excluded from the incomes/expenses pages and the daily
  report’s income/expense lists; the safes list shows them as «صافي التحويلات».
- **Incomes / Expenses**: ledgers over the safe transactions (income serial / expense serial), with
  date/text filters, manual add/edit/delete, whole‑list print, and **per‑transaction voucher**
  (سند قبض / سند صرف, opens in a new tab).
- **Unified money model**: every income = إيصال استلام, every expense = إيصال دفع, numbered by the
  transaction’s serial. Auto sources: collections (income), supplier payments & project expenses
  (expense). Safe balance = initial + incomes − expenses.

### 2.8 Marketing (`Marketing` area)
- **Campaigns** (platform, type, objective, status) with dated **updates** (spend, leads, metrics).

### 2.9 Reports (`Reports` area)
- **Daily report**: one‑day summary of contracts, purchase invoices, income & expense receipts, plus
  **each safe’s balance** at end of day; date picker + print.
- **Customer report**: per customer — contracts, contract value, remaining installments, collected,
  residual; date‑range filter, **column totals**, print.
- **Supplier report**: per supplier — invoices, invoice value, paid, residual; date‑range filter,
  **column totals**, print.

### 2.10 Administration & Settings
- **Users**: CRUD, activate/disable, assign granular permissions per user.
- **Tenants (المؤسسات)**: host‑only management of organizations; per‑tenant onboarding/switching.
- **Settings / Branding**: organization data and logo (used across all printed documents).
- **Startup page (الصفحة الافتتاحية)** per tenant: chosen on the organization settings page (Settings.Manage)
  from a catalog of main pages; users land there after login and on the site root (`/` → Home/Index
  redirects). A user without access to the chosen page falls back to the dashboard, then the first page
  they can open, then «مهامي». Stored in the tenant's `Settings` (key `StartupPage`).
- **Activity log** viewer.
- **Dark / day theme**: ☀️/🌙 toggle in the top bar (and on the login page); the choice is saved per browser,
  applied before first paint (no flash) and synced across open tabs. Switching is soft — a circle reveal
  from the toggle (View Transitions), else a short color fade; reduced-motion users get an instant switch.
  All colors are CSS tokens in `dashboard.css` (`:root` = dark, `:root[data-theme="light"]` = day); charts
  and SweetAlert dialogs follow the theme.
- **Collapsible side menu** (`wwwroot/js/sidebar.js`): the chevron in the sidebar header folds the menu into
  an icon rail. On the rail, hovering / focusing an icon shows its name as a tooltip; a group icon
  (المشتريات، المالية…) pops out its title + sub-pages instead (click also opens it, for touch). Saved per
  browser, applied before first paint, synced across tabs. On phones (≤ 720px) the menu is an off-canvas
  drawer opened by ☰ in the top bar (backdrop / Esc closes it).
- **Account**: login, logout, change password.

---

## 2.12 Shared filter bar

Every list / report page uses one filter component — `Views/Shared/_FilterBar.cshtml` (+ `Models/FilterBar.cs`,
`wwwroot/js/filter-bar.js`, `.fb-*` styles). A page declares only its own filters with the builder:

```cshtml
var filter = FilterBarVm.For(Url.Action("Index"))
    .DateRange(F(Model.From), F(Model.To), "تاريخ الأمر")      // من ← إلى + quick presets
    .Select("projectId", "المشروع", projects, projId)          // searchable list
    .Text("q", "بحث", Model.Q)                                  // free text
    .Print(Url.Action("PrintList", new { ... }))                 // footer actions
    .Excel(Url.Action("Excel", new { ... }));
<partial name="_FilterBar" model="filter" />
```

Layout: header (title, active-filter count, «↺ مسح الفلاتر» = fresh open), body (date-range box + preset
chips, then a responsive grid of fields), footer (summary, print/Excel, بحث). It posts the same GET parameter
names the pages always used — controller filter logic is untouched. Options: date-time ranges, custom /
auto-submitting presets (reports), «الكل» presets that clear dates, single date fields, hidden route values,
live summaries. Used by 29 views (34 pages, incl. the 5 inventory document lists).

Searchable dropdowns (system-wide, `appEnhanceSearchSelect`): the select's empty option is pinned as the first
menu item — «الكل» in filters, the form's own «— بدون … —» / «— اختر —» text in forms — and stays visible while
typing, so a chosen value can always be cleared from the list. Enter while typing picks the first real match.

## 2.14 Role-oriented workspace navigation

Replaces the long sidebar as the normal navigation (per *ERP Role-Oriented Workspace Navigation — Implementation Rules*).
- **One definition** — `Navigation/NavigationCatalog.cs`: 10 modules, 60 pages, 18 quick actions, each with its
  icon, description and the permissions that unlock it (any-of, from `PermissionNames` — no roles, no second
  permission model). `INavigationService` turns it into the user's authorized navigation once per request
  (claims only); a module appears when the user can see at least one of its pages. Hiding is never the security:
  every page keeps its own `[Authorize]`. Every old-sidebar link is covered; a reflection test checks every
  catalog route exists.
- **Home workspace** (`/Workspace`): greeting + search, authorized **module cards**, **⭐ المفضلة**, **آخر ما استخدمته**,
  (quick actions: the top bar). **Module workspace** (`/Workspace/Module/{key}`): its authorized page cards with small
  state-colored KPIs (`IWorkspaceKpiService` — a few aggregate counts, only for the opened module: open tasks,
  overdue installments, uncollected / unpaid invoices, stock alerts, unposted drafts, record counts) and the
  module's primary create actions. The dashboard stays the operational overview.
- **Header** (`Views/Shared/_TopNav.cshtml`), two tiers:
  - **App bar** (sticky, blurred): start — the workspace button, and the host's **tenant switcher** (current
    tenant → تبديل المؤسسة / مؤسسة جديدة / إدارة المؤسسات); centre — **global search** (Ctrl+K palette: modules /
    pages / actions filtered in the browser + customers, suppliers, products, projects, contracts, sales & purchase
    invoices, purchase orders from `/Workspace/Search`, per permission); end — **＋ إجراء جديد** (two-column menu of
    authorized operations; list-page actions open the create form via `#new` → `[data-quick-new]`), favorites,
    assigned-tasks bell, theme, and the **account menu** (avatar: تغيير كلمة المرور، مهامي، تسجيل الخروج). On phones
    the bar keeps icons only; favorites and the theme switch move into the account menu.
  - **Page row**: **breadcrumb** generated from the catalog (module / page / record — e.g. العملاء والمبيعات /
    فواتير المبيعات / فاتورة مبيعات SI-…; a page named like its module appears once) with the page's favorite star
    beside it, and today's date — no second big title (the page's own heading names it). Workspace pages
    have their own hero header, so the home workspace shows no page row and a module workspace only the breadcrumb.
- **Old side menu**: kept implemented but switched off (`NavigationCatalog.LegacySidebar = false`): no ☰ button,
  always hidden. Setting it to true brings back the on-demand full menu (remembered per browser; a drawer on phones).
- **Look**: cards with a tinted icon tile, lift + accent edge on hover and a sliding arrow; pill counters; menus,
  lists and the palette use the same icon tiles (one line-icon set, `NavIcons`; theme tokens `--accent-soft`,
  `--accent-line`, `--shadow-card`).
- **Favorites & recent pages** are stored per user (`UserNavItems`, migration `UserNavigationShortcuts`; user id, or
  «host» for the host account; per tenant). Favorites show only while the page is still visible to the user;
  recents are shortcuts only (the destination authorizes). Visits/stars are excluded from the activity log
  (`[SkipActivityLog]`).
- Startup page: «مساحة العمل» added as an option; it's also the fallback after the dashboard (open to everyone).
- SVG line icons (`Navigation/NavIcons.cs`), theme tokens only (color = state), RTL, responsive (cards → 2/1
  columns on phones), dark + day themes. Script: `wwwroot/js/workspace-nav.js`.

## 2.13 Product units (وحدات الصنف) & selling prices

- A product has up to **3 units**: the **smallest** (الصغرى — e.g. سم), a **bigger** one = N × the smallest (متر = 100
  سم) and the **biggest** = M × the bigger one (كيلو = 1000 متر). A **default unit** is pre-selected on lines, and an
  optional **selling price per unit** (pre-filled on sales invoice lines, editable there). Rules: each bigger unit
  needs the one below it and a factor > 1, no unit repeated; the smallest unit can't change once the product has
  stock movements. Shared helper: `RealState.Application.Inventory.ProductUnits` / `ProductUnitSet`.
- **Stock, movements, average cost and valuation are always in the smallest unit.** Every document line — purchase
  orders, purchase & sales invoices, goods receipts / issues, transfers, adjustments, stock counts — picks a unit
  (`UnitLevel`) and snapshots its factor (`UnitFactor`) + name; quantity × factor = smallest-unit quantity, price ÷
  factor = smallest-unit cost. Receipt/adjustment line totals stay exact (entered qty × entered price), so the GRNI
  of a purchase invoice still nets to zero. The engine now costs outflows with the exact average (value ÷ qty),
  not the 4-decimal one, and smallest-unit costs are stored with 6 decimals.
- Line editors: changing a line's unit converts its typed price (5/متر → 0.05/سم); on sales invoices an automatic
  price switches to the new unit's selling price. Stock columns show the stock in the line's unit; sales
  availability and PO-vs-invoice limits are compared in the smallest unit.
- Reports: stock balance / valuation show the quantity in the smallest unit + a breakdown («2 كيلو، 300 متر، 50 سم»);
  movements show each product's unit; the stock card adds the running balance in units; the inventory dashboard
  alerts show quantities and reorder levels in units (reorder level is set in the smallest unit). Document
  details / prints show each line in the unit it was entered in. Migration `ProductMultiUnits`.
- **Inventory reports page** (`InventoryReports/Index`, «تقارير المخزون»): one page with tabs — أرصدة المخزون، تقييم
  المخزون، حركة المخزون، بطاقة الصنف، مطابقة الأستاذ. A tab loads its report (the action's partial, fetched with
  X-Requested-With) the first time it's opened and keeps it; its filters / «مسح الفلاتر» refresh only that tab; the
  address bar follows (`?tab=…&filters`), so refresh / shared links reopen the same tab and filters. Opening an old
  report URL directly redirects to the page with that tab. Script: `wwwroot/js/inv-reports.js`.

## 2.11 Document numbering (year rule)

Every document number is **year‑prefixed and restarts at 1 each year**, using the document's own date:
`number = year × 10^digits + sequence` (helper `YearSerial` in `RealState.Application.Common`).

| Series | Digits | Example (2026 → 2027) |
|---|---|---|
| سند قبض / سند صرف (income / expense vouchers) + supplier/contractor payment receipts | 7 | 20260000001 → 20270000001 |
| Purchase invoice PI‑, sales invoice SI‑, safe transfer SF‑, inventory documents | 6 | 2026000001 → 2027000001 |
| Work order WO‑, journal entry, collection receipt C‑ | 5 | 202600001 → 202700001 |
| Purchase order PO‑, advance ADV‑, reward RWD‑, task T‑, sale contract S‑ | 4 | 20260001 → 20270001 |

Voucher serials are 64‑bit (`bigint`); migration `VoucherSerials11Digits` converted the earlier 9‑digit
vouchers in place (202600007 → 20260000007, same sequence). Contract codes, rewards, tasks and collection
receipts issued before the year rule keep their original numbers.

---

## 2.15 Tenant isolation & accounting-cycle rules (QC, Oct 2026)
Verified end to end on a scratch database (two tenants; every money-moving add / edit / delete followed by ledger
invariants: balanced trial balance and entries, no orphan entries, one entry per document / cash movement for its
value, safe GL = safe balance, inventory GL = stock value per warehouse, real-estate inventory per unit).
- **Tenant isolation**: all business tables are tenant-filtered; records of another tenant answer «not found» (pages,
  edit forms, deletes, search); users are scoped by `CanManage`. The **host must choose a tenant** before any
  tenant page (`HostTenantFilter`) — before, its actions fell into the fallback tenant.
- **Tenant delete** purges every tenant-scoped table (accounting, inventory, contracting, returns… were missing, and
  some FKs could make the delete fail).
- **Accounting fixes**: manual incomes/expenses post to their category's account and re-post on edit; advance
  disbursement / cash repayment / reward payout post to the employee (سلف الموظفين); a from-salary installment marked
  paid posts Dr رواتب ومكافآت / Cr سلف الموظفين (only for a disbursed advance); editing a sold unit's cost re-posts its
  contract; deleting a safe removes its opening entry; lowering a safe's opening balance obeys the overdraft rule.
- **Guards**: a project with work orders, or a customer with sale contracts, can't be deleted.
- **Repair existing data**: «إعادة بناء القيود» (بيانات المؤسسة) now re-posts every manual and HR cash movement from
  its current values and posts missing salary-deduction entries — idempotent; run it once per tenant after upgrading.

## 3. Permissions Catalog

Grouped, each is an authorization policy + assignable privilege:

| Group | Permissions |
|---|---|
| الرئيسية | Dashboard.View |
| المشاريع | Projects.View / Create / Edit / Delete |
| المبيعات (العقود) | Sales.View / Create / Delete |
| فواتير المبيعات (الأصناف) | SalesInvoices.View / Create / Edit / Delete / Collect |
| التحصيلات | Collections.View / Collect / Cancel |
| الخزائن | Safes.View / Create / Edit / Delete / Transfer / Overdraft |
| المصروفات | Expenses.View / Create / Edit / Delete |
| الإيرادات | Incomes.View / Create / Edit / Delete |
| العملاء | Customers.View / Create / Edit / Delete |
| مندوبو المبيعات | Salespersons.View / Create / Edit / Delete |
| المشتريات | Suppliers.View / Create / Edit / Delete / Pay (suppliers, purchase orders, payments); PurchaseInvoices.View / Create / Edit / Delete |
| التقارير | Reports.View |
| التسويق | Campaigns.View / Create / Edit / Delete |
| المستخدمون والصلاحيات | Users.View / Create / Edit / Delete / ResetPassword (set another user's password; own password is always via «تغيير كلمة المرور» with the current one) |
| إدارة النظام | ActivityLog.View, Settings.Manage, Tenants.Manage |

---

## 4. Core Data Model (selected)

- **Tenancy/Security**: `Tenant`, `ApplicationUser/Role`, `Permission`, `RolePermission`, `Setting`,
  `AuditLog`, `ActivityLog`.
- **Projects**: `Project`, `ProjectUnit`, `ProjectStage`, `StageActivity`, `StageDefinition`,
  `ProjectAttachment` (`StageExpense` retained for history).
- **Sales/CRM**: `SaleContract`, `Installment`, `Customer`, `Lead`, `Employee` (salespersons).
- **Purchasing**: `Supplier`, `SupplierOrder` + `SupplierOrderItem` (purchase order), `PurchaseInvoice` +
  `PurchaseInvoiceItem`, `SupplierPayment` (settles an invoice, or a legacy order).
- **Accounting**: `Safe`, `SafeTransaction` (Income/Expense serial ledger, optionally linked to
  installment / project / stage).
- **Marketing**: `Campaign`, `CampaignUpdate`.
- **Lookups**: `Country`, `City`, `Currency`, `Section`.

---

*Reserved / partially scaffolded for future passes: Finance area, `SalesInvoice` /
`Income` / `Expense` business‑invoice entities, `TaskItem`, `Notification`,
`Attachment`, HR beyond salespersons.*
