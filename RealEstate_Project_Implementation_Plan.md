# Real Estate Business Management System

## Project Implementation Plan

## Overview

This project is a **Business Operations Management System** for a real
estate company that sells and purchases lands and buildings.

It is **not an ERP**. The system focuses on operational data entry and
executive dashboards without accounting journal entries or financial
statements.

## Technology Stack

-   ASP.NET Core MVC (.NET 9)
-   SQL Server
-   Entity Framework Core
-   ASP.NET Identity
-   Grafana
-   Serilog
-   Hangfire

------------------------------------------------------------------------

# Architecture

``` text
Grafana
   │
SQL Views
   │
SQL Server
   │
Entity Framework Core
   │
ASP.NET Core MVC
```

------------------------------------------------------------------------

# Development Phases

## Phase 1 -- Infrastructure

Solution

``` text
RealState.sln

src/
    RealState.Web
    RealState.Application
    RealState.Domain
    RealState.Infrastructure
    RealState.Shared
```

------------------------------------------------------------------------

## Phase 2 -- Database

### Core Tables

-   Tenants
-   Users
-   Roles
-   Permissions
-   RolePermissions
-   UserRoles
-   AuditLogs
-   Settings

### Business Tables

-   Projects
-   Customers
-   Leads
-   SalesInvoices
-   PurchaseInvoices
-   Income
-   Expenses
-   Tasks
-   Notifications
-   Attachments

### Lookup Tables

-   Countries
-   Cities
-   Currencies
-   Sections

------------------------------------------------------------------------

# Multi-Tenant

Every business table contains:

-   TenantId

Use EF Core Global Query Filters to isolate tenant data.

------------------------------------------------------------------------

# Default Seed Data

## Default Tenant

-   Name: SuperTenant

## Default User

-   Username: admin
-   Password: ChangeMe123!

## Default Role

-   SuperAdmin

Grant all permissions.

------------------------------------------------------------------------

# Authentication

-   Login
-   Logout
-   Change Password
-   Reset Password
-   Lock/Unlock User
-   Optional 2FA

------------------------------------------------------------------------

# Authorization

Role and Permission based.

Example Roles:

-   SuperAdmin
-   TenantAdmin
-   Sales Manager
-   Sales Employee
-   Purchase Manager
-   Finance
-   HR
-   Viewer

Use policies:

``` csharp
[Authorize(Policy = "Sales.Create")]
```

------------------------------------------------------------------------

# User Management

Features

-   Create User
-   Edit User
-   Reset Password
-   Assign Roles
-   Assign Permissions
-   Enable/Disable User

------------------------------------------------------------------------

# Business Modules

## Sales

-   Customers
-   Projects
-   Sales Invoices
-   Reservations
-   Contracts
-   Payments

## Purchases

-   Suppliers
-   Purchase Invoices
-   Payments

## Finance

(No accounting entries)

-   Income
-   Expenses
-   Categories
-   Payment Methods

## CRM

-   Leads
-   Calls
-   Meetings
-   Follow-ups

## HR

-   Employees
-   Attendance
-   Vacations
-   Tasks

------------------------------------------------------------------------

# Notifications

-   Due Payments
-   Late Tasks
-   Contract Expiration
-   Email Notifications

------------------------------------------------------------------------

# Audit Logs

Store:

-   User
-   Action
-   Entity
-   Old Value
-   New Value
-   Timestamp
-   IP Address

------------------------------------------------------------------------

# Dashboard Strategy

Grafana should query SQL Views instead of normalized tables.

Example Views

-   vw_DailySales
-   vw_MonthlySales
-   vw_IncomeExpense
-   vw_ProjectSales
-   vw_LeadStatistics
-   vw_UserPerformance

------------------------------------------------------------------------

# Executive Dashboard

Top KPIs

-   Today's Sales
-   Monthly Sales
-   Income
-   Expenses
-   Collections
-   Leads
-   Alerts

Charts

-   Sales Trend
-   Lead Conversion
-   Income vs Expense
-   Top Projects
-   Top Salespersons
-   Top Customers

------------------------------------------------------------------------

# Reporting

MVC Reports

-   Sales
-   Purchases
-   Income
-   Expenses
-   Projects
-   Employees

Grafana Dashboards

-   Executive
-   Finance
-   Sales
-   Marketing
-   Projects

------------------------------------------------------------------------

# Logging

Use Serilog.

------------------------------------------------------------------------

# Background Jobs

Use Hangfire for

-   Scheduled Reports
-   Email Reminders
-   Cleanup
-   Backups

------------------------------------------------------------------------

# Attachments

Store

-   Contracts
-   Invoices
-   Images
-   PDF Documents

------------------------------------------------------------------------

# Common Database Fields

Every business table should contain

-   Id
-   TenantId
-   CreatedAt
-   CreatedBy
-   UpdatedAt
-   UpdatedBy
-   IsDeleted
-   DeletedAt
-   DeletedBy
-   RowVersion

------------------------------------------------------------------------

# Recommended NuGet Packages

-   Microsoft.AspNetCore.Identity.EntityFrameworkCore
-   Microsoft.EntityFrameworkCore.SqlServer
-   AutoMapper
-   FluentValidation
-   Serilog.AspNetCore
-   Hangfire
-   ClosedXML
-   QuestPDF

------------------------------------------------------------------------

# Folder Structure

``` text
Areas/
    Admin
    Sales
    Purchases
    Finance
    CRM
    HR
Controllers/
Views/
Middleware/
Filters/
wwwroot/
```

------------------------------------------------------------------------

# Development Roadmap

  Phase   Module
  ------- ----------------------------
  1       Solution Setup
  2       Multi-Tenant
  3       Users, Roles, Permissions
  4       Settings & Lookups
  5       Sales
  6       Purchases
  7       Income & Expenses
  8       CRM
  9       HR
  10      Notifications & Audit Logs
  11      SQL Reporting Views
  12      Grafana Dashboards
  13      Hangfire
  14      Attachments
  15      Deployment
