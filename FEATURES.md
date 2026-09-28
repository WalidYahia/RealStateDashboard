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

### 2.5 CRM (`CRM` area)
- **Customers**: CRUD, unique phone, source, assigned salesperson; **statement (كشف حساب)** of all
  contracts with paid/remaining, printable statement, receipts, and due‑payment notices.
- **Salespersons (المناديب)** and **Leads** (marketing pipeline with statuses New→Won/Lost).

### 2.6 Suppliers & Contractors — الموردون والمقاولون (`Suppliers` area)
- **Suppliers** list/CRUD (name, phone, email, notes) + supplier **account statement (كشف حساب)**.
Purchasing runs in two stages — **purchase order → purchase invoice**.
- **Purchase orders (أوامر التوريد)** — stage 1, a plain request: number `PO‑YYYY####` (shown on the
  form before saving), date, optional project (searchable), product lines (الأصناف: product picked
  by code/name + **quantity only** — no unit cost or totals). **No supplier**, and saving
  posts nothing (no ledger, stock or safe effect). The list shows each order’s **related invoices**;
  an order with invoices can’t be deleted. Orders from before the invoice stage keep their supplier,
  payable entry, payments and unit costs (shown on the order and in the supplier statement). When an
  invoice copies an order's lines, the unit costs are left empty to be entered from the supplier invoice.
- **Purchase invoices (فواتير المشتريات)** — stage 2, own menu item: number `PI‑YYYY######`
  (e.g. `PI‑2026000001`, shown on the form before saving), date, supplier (required), optional project
  and optional purchase order (all searchable; picking an order offers to copy its lines + project).
  Product lines show product, unit cost (entered manually), **current stock** (all warehouses), quantity,
  total, plus a **warehouse** (required when there are stock‑tracked products). Several invoices may
  reference the same order. Saving:
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
- **Activity log** viewer.
- **Account**: login, logout, change password.

---

## 3. Permissions Catalog

Grouped, each is an authorization policy + assignable privilege:

| Group | Permissions |
|---|---|
| الرئيسية | Dashboard.View |
| المشاريع | Projects.View / Create / Edit / Delete |
| المبيعات (العقود) | Sales.View / Create / Delete |
| التحصيلات | Collections.View / Collect / Cancel |
| الخزائن | Safes.View / Create / Edit / Delete / Transfer / Overdraft |
| المصروفات | Expenses.View / Create / Edit / Delete |
| الإيرادات | Incomes.View / Create / Edit / Delete |
| العملاء | Customers.View / Create / Edit / Delete |
| مندوبو المبيعات | Salespersons.View / Create / Edit / Delete |
| المشتريات | Suppliers.View / Create / Edit / Delete / Pay (suppliers, purchase orders, payments); PurchaseInvoices.View / Create / Edit / Delete |
| التقارير | Reports.View |
| التسويق | Campaigns.View / Create / Edit / Delete |
| المستخدمون والصلاحيات | Users.View / Create / Edit / Delete |
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
