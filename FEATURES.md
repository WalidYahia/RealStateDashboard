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
- **Purchase / supply orders (أوامر التوريد)**: number `PO‑####`, date, optional project, line items
  (البنود: name + cost), total; create/edit with dynamic item rows; per‑order & list prints.
- **Per‑order payments**: pay from the orders list or the statement’s **order picker**, capped at the
  order’s remaining and disabled once fully paid. Each payment:
  - records an **expense** on the order’s **project** (appears in the project’s expenses & summary),
  - issues a printable **إيصال دفع** (`P‑#####`, opens in a new tab),
  - shows on the supplier’s statement.
- **Account statement** = a running‑balance ledger (المصدر / التاريخ / البيان / رصيد قبل / المبلغ /
  الرصيد) with date‑range filter and period **closing balance**; editing an order below its paid
  amount is blocked.

### 2.7 Accounting / Finance (`Accounting` area)
- **Safes (الخزائن)**: CRUD, initial amount, active flag; **movements** view with per‑transaction
  **running balance** and print.
- **Incomes / Expenses**: ledgers over the safe transactions (income serial / expense serial), with
  date/text filters, manual add/edit/delete, whole‑list print, and **per‑transaction voucher**
  (سند قبض / سند صرف, opens in a new tab).
- **Unified money model**: every income = إيصال استلام, every expense = إيصال دفع, numbered by the
  transaction’s serial. Auto sources: collections (income), supplier payments & project expenses
  (expense). Safe balance = initial + incomes − expenses.

### 2.8 Marketing (`Marketing` area)
- **Campaigns** (platform, type, objective, status) with dated **updates** (spend, leads, metrics).

### 2.9 Reports (`Reports` area)
- **Daily report**: one‑day summary of contracts, supplier orders, income & expense receipts, plus
  **each safe’s balance** at end of day; date picker + print.
- **Customer report**: per customer — contracts, contract value, remaining installments, collected,
  residual; date‑range filter, **column totals**, print.
- **Supplier report**: per supplier — orders, order value, paid, residual; date‑range filter,
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
| الخزائن | Safes.View / Create / Edit / Delete |
| المصروفات | Expenses.View / Create / Edit / Delete |
| الإيرادات | Incomes.View / Create / Edit / Delete |
| العملاء | Customers.View / Create / Edit / Delete |
| مندوبو المبيعات | Salespersons.View / Create / Edit / Delete |
| الموردون والمقاولون | Suppliers.View / Create / Edit / Delete / Pay |
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
- **Suppliers**: `Supplier`, `SupplierOrder`, `SupplierOrderItem`, `SupplierPayment`.
- **Accounting**: `Safe`, `SafeTransaction` (Income/Expense serial ledger, optionally linked to
  installment / project / stage).
- **Marketing**: `Campaign`, `CampaignUpdate`.
- **Lookups**: `Country`, `City`, `Currency`, `Section`.

---

*Reserved / partially scaffolded for future passes: Purchases & Finance areas, `SalesInvoice` /
`PurchaseInvoice` / `Income` / `Expense` business‑invoice entities, `TaskItem`, `Notification`,
`Attachment`, HR beyond salespersons.*
