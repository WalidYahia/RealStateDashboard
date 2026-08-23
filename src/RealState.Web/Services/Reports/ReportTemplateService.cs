using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PDFtoImage;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using SkiaSharp;

// PDFtoImage renders via PDFium; it is supported on every platform we deploy to (Windows/Linux).
#pragma warning disable CA1416

namespace RealState.Web.Services.Reports;

public class ReportTemplateService : IReportTemplateService
{
    private const long MaxBytes = 10 * 1024 * 1024;   // 10 MB
    private const int RenderDpi = 150;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IMemoryCache _cache;
    private readonly string _dir;

    public ReportTemplateService(IApplicationDbContext db, ICurrentUserService currentUser, IMemoryCache cache, IWebHostEnvironment env)
    {
        _db = db;
        _currentUser = currentUser;
        _cache = cache;
        // Private application storage — NOT under wwwroot.
        _dir = Path.Combine(env.ContentRootPath, "App_Data", "Templates");
    }

    private static string CacheKey(Guid tenantId) => $"report_template_bg_{tenantId}";

    public Task<ReportTemplate?> GetActiveAsync(CancellationToken ct = default) =>
        _db.ReportTemplates.Where(t => t.IsActive).OrderByDescending(t => t.UploadedAt).FirstOrDefaultAsync(ct);

    public Task<bool> HasActiveAsync(CancellationToken ct = default) =>
        _db.ReportTemplates.AnyAsync(t => t.IsActive, ct);

    public async Task<byte[]?> GetActiveBackgroundAsync(CancellationToken ct = default)
    {
        var key = CacheKey(_currentUser.TenantId);
        if (_cache.TryGetValue(key, out byte[]? cached)) return cached;

        var active = await GetActiveAsync(ct);
        if (active is null) return null;
        var path = Path.Combine(_dir, active.StoredFileName + ".png");
        if (!File.Exists(path)) return null;

        var bytes = await File.ReadAllBytesAsync(path, ct);
        _cache.Set(key, bytes, TimeSpan.FromMinutes(30));
        return bytes;
    }

    public async Task<(bool Ok, string? Error)> UploadAsync(IFormFile? file, CancellationToken ct = default)
    {
        // 1) A file was selected + 2) size.
        if (file is not { Length: > 0 }) return (false, "لم يتم اختيار ملف.");
        if (file.Length > MaxBytes) return (false, "حجم الملف يجب ألا يتجاوز 10 ميجابايت.");

        byte[] pdf;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            pdf = ms.ToArray();
        }

        // 3) Type/content: must actually be a PDF (magic bytes %PDF), not just a trusted extension.
        if (pdf.Length < 5 || pdf[0] != 0x25 || pdf[1] != 0x50 || pdf[2] != 0x44 || pdf[3] != 0x46 || pdf[4] != 0x2D)
            return (false, "الملف ليس PDF صالحًا.");

        // 4) Readable + 5) exactly one page.
        int pageCount;
        try { pageCount = Conversion.GetPageCount(pdf); }
        catch { return (false, "تعذّرت قراءة ملف الـPDF."); }
        if (pageCount != 1) return (false, "يجب أن يحتوي القالب على صفحة واحدة فقط.");

        // 6) Store under a generated unique name (PDF + rendered PNG background).
        Directory.CreateDirectory(_dir);
        var name = Guid.NewGuid().ToString("N");
        var pdfPath = Path.Combine(_dir, name + ".pdf");
        var pngPath = Path.Combine(_dir, name + ".png");
        await File.WriteAllBytesAsync(pdfPath, pdf, ct);
        try
        {
            using var bitmap = Conversion.ToImage(pdf, page: 0, options: new(Dpi: RenderDpi, WithAspectRatio: true, BackgroundColor: SKColors.White));
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            await using var fs = File.Create(pngPath);
            data.SaveTo(fs);
        }
        catch
        {
            TryDelete(pdfPath);
            return (false, "تعذّر تحويل القالب إلى صورة.");
        }

        // 7) + 8) Save metadata and make it the only active template.
        foreach (var prev in await _db.ReportTemplates.Where(t => t.IsActive).ToListAsync(ct))
            prev.IsActive = false;
        _db.ReportTemplates.Add(new ReportTemplate
        {
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFileName = name,
            FileSize = file.Length,
            PageCount = pageCount,
            IsActive = true,
            UploadedAt = DateTime.UtcNow,
            UploadedByUserId = _currentUser.UserId
        });
        await _db.SaveChangesAsync(ct);

        _cache.Remove(CacheKey(_currentUser.TenantId));   // invalidate cached background
        return (true, null);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
