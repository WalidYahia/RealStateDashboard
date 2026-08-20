using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Entities;
using RealState.Application.Enums;

namespace RealState.Web;

/// <summary>Arabic display labels for domain enums, plus dropdown option helpers.</summary>
public static class ArabicLabels
{
    /// <summary>Installment #0 is the scheduled down payment; others show their number.</summary>
    public static string InstNo(int number) => number <= 0 ? "المقدم" : number.ToString();
    public static string InstPhrase(int number) => number <= 0 ? "المقدم" : $"القسط رقم {number}";

    public static string Ar(this LeadStatus s) => s switch
    {
        LeadStatus.New => "جديد",
        LeadStatus.Contacted => "تم التواصل",
        LeadStatus.Qualified => "مؤهل",
        LeadStatus.Proposal => "عرض سعر",
        LeadStatus.Won => "تم الكسب",
        LeadStatus.Lost => "خسارة",
        _ => s.ToString()
    };

    public static List<SelectListItem> LeadStatusOptions(LeadStatus? selected = null) =>
        Enum.GetValues<LeadStatus>()
            .Select(s => new SelectListItem
            {
                Value = ((int)s).ToString(),
                Text = s.Ar(),
                Selected = selected.HasValue && selected.Value == s
            })
            .ToList();

    // ----- Marketing campaign enums -----
    public static string Ar(this CampaignPlatform p) => p switch
    {
        CampaignPlatform.Facebook => "فيسبوك",
        CampaignPlatform.Instagram => "إنستغرام",
        CampaignPlatform.Google => "جوجل",
        CampaignPlatform.Website => "موقع إلكتروني",
        CampaignPlatform.Other => "أخرى",
        _ => p.ToString()
    };

    public static string Ar(this CampaignType t) => t switch
    {
        CampaignType.Awareness => "تعريفية",
        CampaignType.LeadGeneration => "جذب عملاء",
        CampaignType.Conversion => "تحويلات",
        CampaignType.Engagement => "تفاعل",
        CampaignType.Retargeting => "إعادة استهداف",
        _ => t.ToString()
    };

    public static string Ar(this CampaignObjective o) => o switch
    {
        CampaignObjective.Awareness => "زيادة الوعي",
        CampaignObjective.Leads => "جذب العملاء المحتملين",
        CampaignObjective.Sales => "مبيعات",
        CampaignObjective.Reservations => "حجوزات",
        CampaignObjective.Traffic => "زيارات",
        _ => o.ToString()
    };

    public static string Ar(this CampaignStatus s) => s switch
    {
        CampaignStatus.Draft => "مسودة",
        CampaignStatus.Scheduled => "مجدولة",
        CampaignStatus.Active => "نشطة",
        CampaignStatus.Paused => "موقوفة",
        CampaignStatus.Completed => "مكتملة",
        _ => s.ToString()
    };

    public static List<SelectListItem> Options<TEnum>(Func<TEnum, string> label, TEnum? selected = null)
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>()
            .Select(e => new SelectListItem
            {
                Value = Convert.ToInt32(e).ToString(),
                Text = label(e),
                Selected = selected.HasValue && EqualityComparer<TEnum>.Default.Equals(selected.Value, e)
            })
            .ToList();

    public static List<SelectListItem> PlatformOptions(CampaignPlatform? s = null) => Options<CampaignPlatform>(Ar, s);
    public static List<SelectListItem> CampaignTypeOptions(CampaignType? s = null) => Options<CampaignType>(Ar, s);
    public static List<SelectListItem> ObjectiveOptions(CampaignObjective? s = null) => Options<CampaignObjective>(Ar, s);
    public static List<SelectListItem> CampaignStatusOptions(CampaignStatus? s = null) => Options<CampaignStatus>(Ar, s);

    // ----- HR / Customers enums -----
    public static string Ar(this EmployeeType t) => t switch
    {
        EmployeeType.Salesperson => "مندوب مبيعات",
        EmployeeType.SalesManager => "مدير مبيعات",
        EmployeeType.Accountant => "محاسب",
        EmployeeType.HrOfficer => "موارد بشرية",
        EmployeeType.Other => "أخرى",
        _ => t.ToString()
    };

    public static string Ar(this CustomerSource s) => s switch
    {
        CustomerSource.Salesperson => "مندوب مبيعات",
        CustomerSource.Facebook => "فيسبوك",
        CustomerSource.Instagram => "إنستغرام",
        CustomerSource.Google => "جوجل",
        CustomerSource.Website => "الموقع الإلكتروني",
        CustomerSource.Referral => "ترشيح",
        CustomerSource.WalkIn => "زيارة مباشرة",
        CustomerSource.Other => "أخرى",
        _ => s.ToString()
    };

    /// <summary>Source options with "مندوب مبيعات" pinned to the top.</summary>
    public static List<SelectListItem> CustomerSourceOptions(CustomerSource? selected = null)
    {
        var ordered = new[] { CustomerSource.Salesperson }
            .Concat(Enum.GetValues<CustomerSource>().Where(s => s != CustomerSource.Salesperson));
        return ordered.Select(s => new SelectListItem
        {
            Value = ((int)s).ToString(),
            Text = s.Ar(),
            Selected = selected.HasValue && selected.Value == s
        }).ToList();
    }

    // ----- Leads / CRM -----
    public static string Ar(this LeadInterest i) => i switch
    {
        LeadInterest.Interested => "مهتم",
        LeadInterest.NotInterested => "غير مهتم",
        _ => i.ToString()
    };
    public static string Color(this LeadInterest i) => i == LeadInterest.Interested ? "var(--good)" : "var(--muted)";
    public static List<SelectListItem> LeadInterestOptions(LeadInterest? s = null) => Options<LeadInterest>(Ar, s);

    public static string Ar(this LeadChannel c) => c switch
    {
        LeadChannel.SocialMedia => "وسائل التواصل الاجتماعي",
        LeadChannel.Campaign => "الحملات",
        LeadChannel.Salesperson => "مندوب مبيعات",
        LeadChannel.Other => "أخرى",
        _ => c.ToString()
    };
    public static List<SelectListItem> LeadChannelOptions(LeadChannel? s = null) => Options<LeadChannel>(Ar, s);

    /// <summary>Source sub-options for the social-media channel.</summary>
    public static readonly CustomerSource[] SocialMediaSources = { CustomerSource.Facebook, CustomerSource.Instagram };
    /// <summary>Source sub-options for the "other" channel.</summary>
    public static readonly CustomerSource[] OtherSources = { CustomerSource.Google, CustomerSource.Website, CustomerSource.Referral, CustomerSource.WalkIn, CustomerSource.Other };

    public static List<SelectListItem> SourceOptionsFor(LeadChannel channel, CustomerSource? selected = null)
    {
        var set = channel == LeadChannel.SocialMedia ? SocialMediaSources : OtherSources;
        return set.Select(s => new SelectListItem { Value = ((int)s).ToString(), Text = s.Ar(), Selected = selected == s }).ToList();
    }

    /// <summary>Human-readable source of a customer/lead: campaign name for the campaign channel, else the source label.</summary>
    public static string SourceLabel(this Customer c, IReadOnlyDictionary<Guid, string>? campaigns = null)
    {
        if (c.Channel == LeadChannel.Campaign && c.SourceCampaignId is Guid id)
            return campaigns is not null && campaigns.TryGetValue(id, out var n) ? n : "حملة";
        return c.Source.Ar();
    }

    public static string Ar(this CustomerLogKind k) => k switch
    {
        CustomerLogKind.Manual => "تواصل",
        CustomerLogKind.Created => "إنشاء",
        CustomerLogKind.StatusChange => "تغيير الحالة",
        CustomerLogKind.Conversion => "تحويل لعميل",
        CustomerLogKind.WhatsApp => "واتساب",
        _ => k.ToString()
    };

    // ----- Projects enums -----
    public static string Ar(this ProjectType t) => t switch
    {
        ProjectType.Building => "عمارة",
        ProjectType.Mall => "مول",
        ProjectType.Land => "أرض",
        _ => t.ToString()
    };

    public static string Ar(this UnitStatus s) => s switch
    {
        UnitStatus.NotReady => "غير جاهزة",
        UnitStatus.Available => "متاحة",
        UnitStatus.Sold => "مباعة",
        _ => s.ToString()
    };

    public static List<SelectListItem> ProjectTypeOptions(ProjectType? s = null) => Options<ProjectType>(Ar, s);
    public static List<SelectListItem> UnitStatusOptions(UnitStatus? s = null) => Options<UnitStatus>(Ar, s);

    // ----- Sales -----
    public static string Ar(this InstallmentStep s) => s switch
    {
        InstallmentStep.Monthly => "شهري",
        InstallmentStep.Quarterly => "كل 3 أشهر",
        InstallmentStep.SemiAnnual => "كل 6 أشهر",
        InstallmentStep.Yearly => "سنوي",
        _ => s.ToString()
    };

    public static string Ar(this InstallmentStatus s) => s switch
    {
        InstallmentStatus.Paid => "مدفوع",
        InstallmentStatus.Overdue => "متأخر",
        InstallmentStatus.Pending => "مستحق",
        _ => s.ToString()
    };

    public static string BadgeClass(this InstallmentStatus s) => s switch
    {
        InstallmentStatus.Paid => "state-on",
        InstallmentStatus.Overdue => "state-off",
        _ => "badge-role"
    };

    public static List<SelectListItem> InstallmentStepOptions(InstallmentStep? s = null) => Options<InstallmentStep>(Ar, s);

    // ----- Accounting -----
    public static string Ar(this TxnType t) => t switch
    {
        TxnType.Income => "إيراد",
        TxnType.Expense => "مصروف",
        _ => t.ToString()
    };

    public static string Ar(this TxnSource s) => s switch
    {
        TxnSource.Manual => "يدوي",
        TxnSource.Collection => "تحصيل قسط",
        TxnSource.ProjectExpense => "مصروف مشروع",
        TxnSource.SupplierPayment => "سداد مورد",
        TxnSource.AdvanceDisbursement => "صرف سلفة",
        TxnSource.RewardPayment => "صرف مكافأة",
        TxnSource.AdvanceRepayment => "سداد سلفة",
        _ => s.ToString()
    };

    // ---------- HR ----------
    public static string Ar(this EmploymentType t) => t switch
    {
        EmploymentType.Permanent => "دائم",
        EmploymentType.Temporary => "مؤقت",
        EmploymentType.Training => "تدريب",
        _ => t.ToString()
    };
    public static List<SelectListItem> EmploymentTypeOptions(EmploymentType? s = null) => Options<EmploymentType>(Ar, s);

    public static string Ar(this VacationType t) => t switch
    {
        VacationType.Sick => "مرضية",
        VacationType.Normal => "اعتيادية",
        VacationType.Emergency => "عارضة",
        _ => t.ToString()
    };
    public static List<SelectListItem> VacationTypeOptions(VacationType? s = null) => Options<VacationType>(Ar, s);

    public static string Ar(this LateBracket b) => b switch
    {
        LateBracket.Upto15 => "حتى 15 دقيقة",
        LateBracket.Upto30 => "حتى 30 دقيقة",
        LateBracket.Upto60 => "حتى ساعة",
        LateBracket.Over60 => "أكثر من ساعة",
        _ => b.ToString()
    };

    public static string Ar(this DeductionFraction f) => f switch
    {
        DeductionFraction.None => "بدون",
        DeductionFraction.Quarter => "ربع يوم",
        DeductionFraction.Half => "نصف يوم",
        DeductionFraction.Full => "يوم كامل",
        _ => f.ToString()
    };
    public static decimal Value(this DeductionFraction f) => f switch
    {
        DeductionFraction.Quarter => 0.25m,
        DeductionFraction.Half => 0.5m,
        DeductionFraction.Full => 1m,
        _ => 0m
    };
    public static List<SelectListItem> DeductionFractionOptions(DeductionFraction? s = null) => Options<DeductionFraction>(Ar, s);

    public static string Ar(this AdvanceRepaymentMethod m) => m switch
    {
        AdvanceRepaymentMethod.FromSalary => "خصم من الراتب",
        AdvanceRepaymentMethod.Cash => "نقدًا",
        _ => m.ToString()
    };
    public static List<SelectListItem> AdvanceRepaymentMethodOptions(AdvanceRepaymentMethod? s = null) => Options<AdvanceRepaymentMethod>(Ar, s);

    public static string Ar(this RewardPayVia v) => v switch
    {
        RewardPayVia.Salary => "مع الراتب",
        RewardPayVia.Cash => "نقدًا",
        _ => v.ToString()
    };
    public static List<SelectListItem> RewardPayViaOptions(RewardPayVia? s = null) => Options<RewardPayVia>(Ar, s);

    public static string Ar(this DisbursementStatus s) => s switch
    {
        DisbursementStatus.NotDisbursed => "لم يُصرف",
        DisbursementStatus.Disbursed => "تم الصرف",
        _ => s.ToString()
    };
    public static string Ar(this PayStatus s) => s switch
    {
        PayStatus.NotPaid => "لم يُسدَّد",
        PayStatus.Paid => "تم السداد",
        _ => s.ToString()
    };

    public static string Ar(this EmployeeAttachmentKind k) => k switch
    {
        EmployeeAttachmentKind.NationalId => "بطاقة الرقم القومي",
        EmployeeAttachmentKind.PersonalImage => "صورة شخصية",
        EmployeeAttachmentKind.CriminalRecord => "فيش وتشبيه",
        EmployeeAttachmentKind.Qualification => "مؤهل دراسي",
        EmployeeAttachmentKind.Cv => "السيرة الذاتية",
        EmployeeAttachmentKind.WorkExperience => "خبرات عمل",
        EmployeeAttachmentKind.Other => "أخرى",
        _ => k.ToString()
    };
    public static List<SelectListItem> AttachmentKindOptions(EmployeeAttachmentKind? s = null) => Options<EmployeeAttachmentKind>(Ar, s);

    public static string Ar(this AccountingEntryKind k) => k switch
    {
        AccountingEntryKind.General => "عام",
        AccountingEntryKind.Advance => "سلفة",
        AccountingEntryKind.Reward => "مكافأة",
        _ => k.ToString()
    };

    // ---------- Tasks ----------
    public static string Ar(this WorkTaskStatus s) => s switch
    {
        WorkTaskStatus.Todo => "قيد الانتظار",
        WorkTaskStatus.InProgress => "قيد التنفيذ",
        WorkTaskStatus.Completed => "مكتملة",
        _ => s.ToString()
    };

    /// <summary>Text color for a status badge.</summary>
    public static string Color(this WorkTaskStatus s) => s switch
    {
        WorkTaskStatus.Completed => "var(--good)",
        WorkTaskStatus.InProgress => "var(--series-1)",
        _ => "var(--muted)"
    };

    public static List<SelectListItem> WorkTaskStatusOptions(WorkTaskStatus? s = null) => Options<WorkTaskStatus>(Ar, s);

    public static string Ar(this TaskSeverity s) => s switch
    {
        TaskSeverity.Normal => "عادية",
        TaskSeverity.Urgent => "عاجلة",
        _ => s.ToString()
    };

    /// <summary>Text color for a severity badge.</summary>
    public static string Color(this TaskSeverity s) => s == TaskSeverity.Urgent ? "var(--critical)" : "var(--good)";

    public static List<SelectListItem> TaskSeverityOptions(TaskSeverity? s = null) => Options<TaskSeverity>(Ar, s);
}
