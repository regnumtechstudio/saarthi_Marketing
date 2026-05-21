using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegnumDigital.API.Data;
using RegnumDigital.API.DTOs;
using RegnumDigital.API.Models;
using RegnumDigital.API.Services;
using System.Security.Claims;


namespace RegnumDigital.API.Controllers;

// ─────────────────────────────────────────────────────────────
// PARTNER — DASHBOARD
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/partner/dashboard")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "PartnerOnly")]
public class PartnerDashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    public PartnerDashboardController(AppDbContext db) => _db = db;
    private int Pid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
       // var userid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var pid = Pid;
        var downloads = await _db.PartnerActivities.CountAsync(a => a.PartnerId == pid && a.Action == "downloaded");
        var collections = await _db.PartnerCollections.CountAsync(c => c.PartnerId == pid);
        var upcoming = await _db.Events.CountAsync(e => e.Status == "upcoming" && e.EventDate > DateTime.UtcNow);
        return Ok(new DashboardStats(downloads, collections, upcoming));
    }

    [HttpGet("recent-content")]
    public async Task<IActionResult> GetRecentContent()
    {
        var list = await _db.ContentAssets
            .Include(c => c.Category).Include(c => c.Amc)
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.CreatedAt).Take(6)
            .Select(c => new ContentAssetDto(c.Id, c.Title, c.AssetType,
                c.CategoryId, c.Category != null ? c.Category.Name : null,
                c.AmcId, c.Amc != null ? c.Amc.Name : null,
                c.FilePath, c.EmbedUrl, c.Source, c.IsActive, c.CreatedAt))
            .ToListAsync();
        return Ok(list);
    }
}

// ─────────────────────────────────────────────────────────────
// PARTNER — CONTENT
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/partner/content")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "PartnerOnly")]
public class PartnerContentController : ControllerBase
{
    private readonly AppDbContext _db;
    public PartnerContentController(AppDbContext db) => _db = db;
    private int Pid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q, [FromQuery] int? categoryId, [FromQuery] string? assetType)
    {
        var query = _db.ContentAssets.Include(c => c.Category).Include(c => c.Amc)
            .Where(c => c.IsActive);
        if (!string.IsNullOrEmpty(q))
            query = query.Where(c => c.Title.Contains(q));
        if (categoryId.HasValue)
            query = query.Where(c => c.CategoryId == categoryId);
        if (!string.IsNullOrEmpty(assetType))
            query = query.Where(c => c.AssetType == assetType);

        var list = await query.OrderByDescending(c => c.CreatedAt)
            .Select(c => new ContentAssetDto(c.Id, c.Title, c.AssetType,
                c.CategoryId, c.Category != null ? c.Category.Name : null,
                c.AmcId, c.Amc != null ? c.Amc.Name : null,
                c.FilePath, c.EmbedUrl, c.Source, c.IsActive, c.CreatedAt))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("{id}/download")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public async Task<IActionResult> LogDownload(int id)
    {
        var asset = await _db.ContentAssets.FindAsync(id);
        if (asset == null) return NotFound(new { message = "Content not found." });
        _db.PartnerActivities.Add(new PartnerActivity
        {
            PartnerId = Pid,
            Action = "downloaded",
            AssetId = id,
            AssetName = asset.Title
        });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Download logged.", filePath = asset.FilePath, embedUrl = asset.EmbedUrl });
    }

    [HttpPost("{id}/view")]
    public async Task<IActionResult> LogView(int id)
    {
        var asset = await _db.ContentAssets.FindAsync(id);
        if (asset == null) return NotFound();
        _db.PartnerActivities.Add(new PartnerActivity
        {
            PartnerId = Pid,
            Action = "viewed",
            AssetId = id,
            AssetName = asset.Title
        });
        await _db.SaveChangesAsync();
        return Ok(new { message = "View logged." });
    }
}

// ─────────────────────────────────────────────────────────────
// PARTNER — EVENTS
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/partner/events")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "PartnerOnly")]
public class PartnerEventsController : ControllerBase
{
    private readonly AppDbContext _db;
    public PartnerEventsController(AppDbContext db) => _db = db;
    private int Pid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var pid = Pid;
        var regIds = await _db.EventRegistrations
            .Where(r => r.PartnerId == pid).Select(r => r.EventId).ToListAsync();

        var list = await _db.Events
            .Where(e => e.Status != "cancelled")
            .OrderBy(e => e.EventDate)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.EventType,
                e.EventDate,
                e.SpeakerName,
                e.LocationLink,
                e.Status,
                IsRegistered = regIds.Contains(e.Id)
            }).ToListAsync();
        return Ok(list);
    }

    [HttpPost("{id}/register")]
    public async Task<IActionResult> Register(int id)
    {
        var pid = Pid;
        if (!await _db.Events.AnyAsync(e => e.Id == id))
            return NotFound(new { message = "Event not found." });
        if (await _db.EventRegistrations.AnyAsync(r => r.EventId == id && r.PartnerId == pid))
            return Conflict(new { message = "Already registered for this event." });
        _db.EventRegistrations.Add(new EventRegistration { EventId = id, PartnerId = pid });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Registered successfully!" });
    }

    [HttpDelete("{id}/unregister")]
    public async Task<IActionResult> Unregister(int id)
    {
        var pid = Pid;
        var reg = await _db.EventRegistrations
            .FirstOrDefaultAsync(r => r.EventId == id && r.PartnerId == pid);
        if (reg == null) return NotFound(new { message = "Registration not found." });
        _db.EventRegistrations.Remove(reg);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Unregistered." });
    }
}

// ─────────────────────────────────────────────────────────────
// PARTNER — PROFILE
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/partner/profile")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "PartnerOnly")]
public class PartnerProfileController : ControllerBase
{
    private readonly AppDbContext _db;
    public PartnerProfileController(AppDbContext db) => _db = db;
    private int Pid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var p = await _db.Partners.Include(x => x.Role).Include(x => x.Plan)
            .FirstOrDefaultAsync(x => x.Id == Pid);
        if (p == null) return NotFound(new { message = "Profile not found." });
        return Ok(new PartnerDto(p.Id, p.FullName, p.Email, p.Mobile,
            p.ArnNumber, p.BusinessName, p.Role?.Name, p.Plan?.Name, p.IsActive, p.ApprovalStatus, p.CreatedAt));
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] CreatePartnerRequest req)
    {
        var p = await _db.Partners.FindAsync(Pid);
        if (p == null) return NotFound();
        p.FullName = req.FullName; p.Mobile = req.Mobile;
        p.ArnNumber = req.ArnNumber; p.BusinessName = req.BusinessName;
        if (!string.IsNullOrEmpty(req.Password))
            p.Password = BCrypt.Net.BCrypt.HashPassword(req.Password);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Profile updated." });
    }
}

// ─────────────────────────────────────────────────────────────
// PARTNER — COBRAND
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/partner/cobrand")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "PartnerOnly")]
public class PartnerCobrandController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    public PartnerCobrandController(AppDbContext db, IWebHostEnvironment env)
    { _db = db; _env = env; }
    private int Pid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!) ;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var cb = await _db.PartnerCobrands.FirstOrDefaultAsync(c => c.PartnerId == Pid);
        if (cb == null) return Ok(new CobrandDto(null, null, null, null, "#00B386","#FFFFFF","Sora", null, null));
        return Ok(new CobrandDto(cb.BrandName, cb.ArnNumber, cb.Email, cb.Mobile, cb.PrimaryColor, cb.FontColor, cb.FontStyle,cb.LogoPath, cb.PhotoPath));
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] CobrandSaveRequest req)
    {
        var pid = Pid;
        var cb = await _db.PartnerCobrands.FirstOrDefaultAsync(c => c.PartnerId == pid);
        if (cb == null) { cb = new PartnerCobrand { PartnerId = pid }; _db.PartnerCobrands.Add(cb); }
        cb.BrandName = req.brand_name; cb.ArnNumber = req.arn_number;
        cb.Email = req.Email; cb.Mobile = req.Mobile;
        cb.PrimaryColor = req.primary_color ?? "#00B386";
        cb.FontColor = req.font_color ?? "#FFFFFF";
        cb.FontStyle = req.font_style ?? "Sora";
        cb.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Cobrand settings saved." });
    }

    /// <summary>Upload logo or passport photo for cobrand</summary>
    [HttpPost("upload-assets")]
    [RequestSizeLimit(10_485_760)] // 10 MB
    public async Task<IActionResult> UploadAssets(
        [FromForm] IFormFile? logo,
        [FromForm] IFormFile? photo)
    {
        var pid = Pid;
        var cb = await _db.PartnerCobrands.FirstOrDefaultAsync(c => c.PartnerId == pid);
        if (cb == null) { cb = new PartnerCobrand { PartnerId = pid }; _db.PartnerCobrands.Add(cb); }

        var uploadsPath = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "cobrand");
        Directory.CreateDirectory(uploadsPath);

        if (logo != null)
        {
            var ext = Path.GetExtension(logo.FileName).ToLowerInvariant();
            var fn = $"logo_{pid}_{Guid.NewGuid()}{ext}";
            using var s = System.IO.File.Create(Path.Combine(uploadsPath, fn));
            await logo.CopyToAsync(s);
            cb.LogoPath = $"/uploads/cobrand/{fn}";
        }

        if (photo != null)
        {
            var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
            var fn = $"photo_{pid}_{Guid.NewGuid()}{ext}";
            using var s = System.IO.File.Create(Path.Combine(uploadsPath, fn));
            await photo.CopyToAsync(s);
            cb.PhotoPath = $"/uploads/cobrand/{fn}";
        }

        cb.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Assets uploaded.", logoPath = cb.LogoPath, photoPath = cb.PhotoPath });
    }
}

// ─────────────────────────────────────────────────────────────
// PARTNER — COLLECTIONS
// ─────────────────────────────────────────────────────────────

[ApiController]
[Route("api/partner/collections")]
//[Authorize]//(Policy = "PartnerOnly")]
[Authorize(AuthenticationSchemes = "Bearer")] //,Policy = "PartnerOnly")
public class PartnerCollectionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    public PartnerCollectionsController(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }
    private int Pid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    //(AuthenticationSchemes = "Bearer")] //,Policy = "PartnerOnly")
    [HttpGet]    
    public async Task<IActionResult> GetAll()
    {
       // var userid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var pid = Pid;
        var list = await _db.PartnerCollections.Where(c => c.PartnerId == Convert.ToInt64(pid))
                .Include(c => c.Asset).ThenInclude(a => a!.Category)
                .Select(c => new ContentAssetDto(
                    c.Asset!.Id, c.Asset.Title, c.Asset.AssetType,
                    c.Asset.CategoryId, c.Asset.Category != null ? c.Asset.Category.Name : null,
                    c.Asset.AmcId, null, c.Asset.FilePath, c.Asset.EmbedUrl,
                    c.Asset.Source, c.Asset.IsActive, c.Asset.CreatedAt))
                .ToListAsync();

            return Ok(list);

    }

    [HttpPost("{assetId}")]
    public async Task<IActionResult> Save(int assetId)
    {
         var pid = Pid;
        if (!await _db.ContentAssets.AnyAsync(a => a.Id == assetId))
            return NotFound(new { message = "Content not found." });
        if (await _db.PartnerCollections.AnyAsync(c => c.PartnerId == pid && c.AssetId == assetId))
            return Conflict(new { message = "Already in collection." });

        var asset = await _db.ContentAssets.FindAsync(assetId);
        _db.PartnerCollections.Add(new PartnerCollection { PartnerId = pid, AssetId = assetId });
        _db.PartnerActivities.Add(new PartnerActivity
        {
            PartnerId = pid,
            Action = "saved",
            AssetId = assetId,
            AssetName = asset?.Title
        });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Saved to collection." });
    }

    [HttpDelete("{assetId}")]
    public async Task<IActionResult> Remove(int assetId)
    {
        int userid = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var pid = userid;
        var col = await _db.PartnerCollections
            .FirstOrDefaultAsync(c => c.PartnerId == pid && c.AssetId == assetId);
        if (col == null) return NotFound(new { message = "Not in collection." });
        _db.PartnerCollections.Remove(col);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Removed from collection." });
    }
}

// ─────────────────────────────────────────────────────────────
// PARTNER — ACTIVITY LOG
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/partner/activity")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "PartnerOnly")]
public class PartnerActivityController : ControllerBase
{
    private readonly AppDbContext _db;
    public PartnerActivityController(AppDbContext db) => _db = db;
    private int Pid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int take = 50)
    {
        var pid = Pid;
        var list = await _db.PartnerActivities
            .Where(a => a.PartnerId == pid)
            .OrderByDescending(a => a.CreatedAt).Take(take)
            .Select(a => new ActivityDto(a.Id, a.Action, a.AssetName, a.CreatedAt))
            .ToListAsync();
        return Ok(list);
    }
}

// ─────────────────────────────────────────────────────────────
// LMS — PUBLIC (Admin + Partner both)
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/lms")]
[Authorize]
public class LmsController : ControllerBase
{
    private readonly AppDbContext _db;
    public LmsController(AppDbContext db) => _db = db;

    [HttpGet("courses")]
    public async Task<IActionResult> GetCourses()
    {
        var courses = await _db.LmsCourses.Where(c => c.IsActive)
            .Include(c => c.Lessons).ThenInclude(l => l.Asset)
            .Select(c => new CourseDto(c.Id, c.Title, c.Description,
                c.Lessons.OrderBy(l => l.SortOrder)
                    .Select(l => new LessonDto(l.Id, l.Title, l.LessonType, l.SortOrder,
                        l.Asset != null ? (l.Asset.FilePath ?? l.Asset.EmbedUrl) : null))
                    .ToList()))
            .ToListAsync();
        return Ok(courses);
    }

    // ============================================================
    // PARTNER SUBSCRIPTION CONTROLLER
    // ============================================================
    [Route("api/partner")]
    [Authorize]
    public class PartnerSubscriptionController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ISubscriptionService _sub;
        public PartnerSubscriptionController(AppDbContext db, SubscriptionService sub) { _db = db; _sub = sub; }

        protected int GetPartnerId() =>
           int.Parse(User.FindFirst("sub")?.Value ?? "0");
        protected string GetScope() =>
            User.FindFirst("scope")?.Value ?? "";
        protected IActionResult Err(string msg, int code = 400) =>
            StatusCode(code, new { error = msg });
        protected IActionResult Ok2(object data) =>
            Ok(new { data });

        [HttpGet("subscription")]
        public async Task<IActionResult> GetSubscription()
        {
            var sub = await _sub.GetActiveSubscriptionAsync(GetPartnerId());
            return Ok(sub);
        }

        [HttpPost("cobrand/setup-done")]
        public async Task<IActionResult> MarkCobrandDone()
        {
            var partner = await _db.Partners.FindAsync(GetPartnerId());
            if (partner == null) return NotFound();
           // partner.CobrandSetupDone = true;
            await _db.SaveChangesAsync();
            return Ok(new { message = "Cobrand setup marked complete." });
        }

        public async Task<SubscriptionDto> GetActiveSubscriptionAsync(int partnerId)
        {
            var sub = await _db.PartnerSubscriptions
                .Include(s => s.Plan)
                .Where(s => s.PartnerId == partnerId && s.Status == "Active")
                .FirstOrDefaultAsync();

            if (sub == null) return new SubscriptionDto(false, null, null, null,
                null, null, null, null, null);

            // Auto-expire if EndDate passed
            if (sub.EndDate < DateTime.UtcNow)
            {
                sub.Status = "Expired";
                await _db.SaveChangesAsync();
                return new SubscriptionDto(false, null, null, null, null, null, null, "Expired", null);
            }

            //var features = sub.Plan?.FeaturesJson != null
            //    ? System.Text.Json.JsonSerializer.Deserialize<string[]>(sub.Plan.FeaturesJson) ?? []
            //    : Array.Empty<string>();

            return new SubscriptionDto(
                true, sub.PlanId, sub.Plan?.Name, sub.BillingCycle,
                sub.StartDate, sub.EndDate, sub.AmountPaid, sub.Status,null);//, features);
        }
    }

}
