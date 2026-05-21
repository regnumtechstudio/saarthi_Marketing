using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
//using Microsoft.EntityFrameworkCore;
using RegnumDigital.API.Data;
using RegnumDigital.API.DTOs;
using RegnumDigital.API.Models;
using RegnumDigital.API.Services;
using System.Linq;
using System.Text.Json;

namespace RegnumDigital.API.Controllers;

// ─────────────────────────────────────────────────────────────
// ADMIN — PARTNERS
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/partners")]
//[Authorize(Policy = "AdminOnly")]
[Authorize(AuthenticationSchemes = "Bearer")]//, Policy = "AdminOnly")]
public class AdminPartnersController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminPartnersController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search)
    {
        var q = _db.Partners.Include(p => p.Role).Include(p => p.Plan).AsQueryable();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(p => p.FullName.Contains(search) || p.Email.Contains(search) || (p.Mobile != null && p.Mobile.Contains(search)));
        var list = await q.OrderByDescending(p => p.CreatedAt)
            .Select(p => new PartnerDto(p.Id, p.FullName, p.Email, p.Mobile,
                p.ArnNumber, p.BusinessName, p.Role != null ? p.Role.Name : null,
                p.Plan != null ? p.Plan.Name : null, p.IsActive, p.ApprovalStatus, p.CreatedAt))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOne(int id)
    {
        var p = await _db.Partners.Include(x => x.Role).Include(x => x.Plan)
                    .FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound(new { message = "Partner not found." });
        return Ok(new PartnerDto(p.Id, p.FullName, p.Email, p.Mobile,
            p.ArnNumber, p.BusinessName, p.Role?.Name, p.Plan?.Name, p.IsActive,"", p.CreatedAt));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePartnerRequest req)
    {
        if (await _db.Partners.AnyAsync(p => p.Email == req.Email))
            return Conflict(new { message = "Email already exists." });
        var partner = new Partner
        {
            FullName = req.FullName,
            Email = req.Email,
            Mobile = req.Mobile,
            ArnNumber = req.ArnNumber,
            BusinessName = req.BusinessName,
            RoleId = req.RoleId,
            PlanId = req.PlanId,
            Password = BCrypt.Net.BCrypt.HashPassword(req.Password ?? "Regnum@123"),
            IsActive = true,
            ApprovalStatus = "approved"
        };
        _db.Partners.Add(partner);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Partner created.", id = partner.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreatePartnerRequest req)
    {
        var p = await _db.Partners.FindAsync(id);
        if (p == null) return NotFound(new { message = "Partner not found." });
        p.FullName = req.FullName; p.Email = req.Email; p.Mobile = req.Mobile;
        p.ArnNumber = req.ArnNumber; p.BusinessName = req.BusinessName;
        p.RoleId = req.RoleId; p.PlanId = req.PlanId;
        if (!string.IsNullOrEmpty(req.Password))
            p.Password = BCrypt.Net.BCrypt.HashPassword(req.Password);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Partner updated." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var p = await _db.Partners.FindAsync(id);
        if (p == null) return NotFound(new { message = "Partner not found." });
        p.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Partner deactivated." });
    }
}

// ─────────────────────────────────────────────────────────────
// ADMIN — PLANS
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/plans")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "AdminOnly")]
public class AdminPlansController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminPlansController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.Plans.Include(p => p.Role)
            .Select(p => new PlanDto(p.Id, p.Name, p.MonthlyPrice, p.YearlyPrice,
                p.RoleId, p.Role != null ? p.Role.Name : null, p.IsActive))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SavePlanRequest req)
    {
        var plan = new Plan { Name = req.Name, MonthlyPrice = req.MonthlyPrice, YearlyPrice = req.YearlyPrice, RoleId = req.RoleId };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Plan created.", id = plan.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] SavePlanRequest req)
    {
        var plan = await _db.Plans.FindAsync(id);
        if (plan == null) return NotFound(new { message = "Plan not found." });
        plan.Name = req.Name; plan.MonthlyPrice = req.MonthlyPrice;
        plan.YearlyPrice = req.YearlyPrice; plan.RoleId = req.RoleId;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Plan updated." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var plan = await _db.Plans.FindAsync(id);
        if (plan == null) return NotFound(new { message = "Plan not found." });
        plan.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Plan deactivated." });
    }
}

// ─────────────────────────────────────────────────────────────
// ADMIN — PROMO CODES
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/promos")]
//[Authorize(Policy = "AdminOnly")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class AdminPromosController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminPromosController(AppDbContext db) => _db = db;

    //[HttpGet]
    //public async Task<IActionResult> GetAll()
    //{
    //    var list = await _db.PromoCodes.ToListAsync();
    //    return Ok(list.Select(p => new PromoDto(p.Id, p.Code, p.DiscountType, p.DiscountValue,
    //            p.MaxUses, p.UsedCount, p.ExpiresAt, p.IsActive))
    //        .OrderByDescending(p => p.Id));
    //}
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var promos = await _db.PromoCodes.OrderByDescending(p => p.Id).ToListAsync();
        return Ok(promos.Select(p => new
        {
            p.Id,
            p.Code,
            p.DiscountType,
            p.DiscountValue,
            p.ExpiresAt,
            p.MaxUses,
            p.CurrentUses,
            usedCount = p.CurrentUses,
            p.IsActive,
            p.IsVisibleOnCheckout,
            p.VisibleDescription,
            p.PerUserLimit,
            applicablePlanIds = p.ApplicablePlansJson != null
                ? JsonSerializer.Deserialize<int[]>(p.ApplicablePlansJson)
                : null
        }));
    }


    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SavePromoRequest req)
    {
        if (await _db.PromoCodes.AnyAsync(p => p.Code == req.Code.ToUpper()))
            return Conflict(new { message = "Promo code already exists." });
        var promo = new PromoCode
        {
            Code = req.Code.ToUpper(),
            DiscountType = req.DiscountType,
            DiscountValue = req.DiscountValue,
            MaxUses = req.MaxUses,
            ExpiresAt = req.ExpiresAt
        };
        _db.PromoCodes.Add(promo);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Promo code created.", id = promo.Id });
    }

    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var p = await _db.PromoCodes.FindAsync(id);
        if (p == null) return NotFound(new { message = "Promo not found." });
        p.IsActive = 0;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Promo deactivated." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await _db.PromoCodes.FindAsync(id);
        if (p == null) return NotFound(new { message = "Promo not found." });
        _db.PromoCodes.Remove(p);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Promo deleted." });
    }
}

// ─────────────────────────────────────────────────────────────
// ADMIN — EVENTS
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/events")]
//[Authorize(Policy = "AdminOnly")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class AdminEventsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminEventsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.Events.ToListAsync();
        
        return Ok(list.Select(e => new EventDto(e.Id, e.Title, e.EventType, e.EventDate,
                e.SpeakerName, e.LocationLink, e.Status))
            .OrderByDescending(e => e.EventDate));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveEventRequest req)
    {
        var ev = new Event
        {
            Title = req.Title,
            EventType = req.EventType,
            EventDate = req.EventDate,
            SpeakerName = req.SpeakerName,
            LocationLink = req.LocationLink
        };
        _db.Events.Add(ev);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Event created.", id = ev.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveEventRequest req)
    {
        var ev = await _db.Events.FindAsync(id);
        if (ev == null) return NotFound(new { message = "Event not found." });
        ev.Title = req.Title; ev.EventType = req.EventType; ev.EventDate = req.EventDate;
        ev.SpeakerName = req.SpeakerName; ev.LocationLink = req.LocationLink;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Event updated." });
    }

    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var ev = await _db.Events.FindAsync(id);
        if (ev == null) return NotFound(new { message = "Event not found." });
        ev.Status = "cancelled";
        await _db.SaveChangesAsync();
        return Ok(new { message = "Event cancelled." });
    }

    [HttpPut("{id}/complete")]
    public async Task<IActionResult> Complete(int id)
    {
        var ev = await _db.Events.FindAsync(id);
        if (ev == null) return NotFound(new { message = "Event not found." });
        ev.Status = "completed";
        await _db.SaveChangesAsync();
        return Ok(new { message = "Event marked complete." });
    }
}

// ─────────────────────────────────────────────────────────────
// ADMIN — CATEGORIES
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/categories")]
//[Authorize(Policy = "AdminOnly")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class AdminCategoriesController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminCategoriesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var all = await _db.Categories.Include(c => c.Children).ToListAsync();
        var parents = all.Where(c => c.ParentId == null)
            .Select(c => new CategoryDto(c.Id, c.Name, null, null,
                c.Children.Select(ch => new CategoryDto(ch.Id, ch.Name, c.Id, c.Name, null)).ToList()))
            .ToList();
        return Ok(parents);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveCategoryRequest req)
    {
        var cat = new Category { Name = req.Name, ParentId = req.ParentId };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Category saved.", id = cat.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveCategoryRequest req)
    {
        var cat = await _db.Categories.FindAsync(id);
        if (cat == null) return NotFound(new { message = "Category not found." });
        cat.Name = req.Name; cat.ParentId = req.ParentId;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Category updated." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var cat = await _db.Categories.FindAsync(id);
        if (cat == null) return NotFound(new { message = "Category not found." });
        _db.Categories.Remove(cat);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Category deleted." });
    }
}

// ─────────────────────────────────────────────────────────────
// ADMIN — AMC MASTER
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/amc")]
////[Authorize(Policy = "AdminOnly")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class AdminAmcController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminAmcController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.AmcMasters
            .Select(a => new AmcDto(a.Id, a.Name, a.InstagramUrl, a.YoutubeChannelId, a.AutoSyncEnabled))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveAmcRequest req)
    {
        var amc = new AmcMaster
        {
            Name = req.Name,
            InstagramUrl = req.InstagramUrl,
            YoutubeChannelId = req.YoutubeChannelId,
            AutoSyncEnabled = req.AutoSyncEnabled
        };
        _db.AmcMasters.Add(amc);
        await _db.SaveChangesAsync();
        return Ok(new { message = "AMC saved.", id = amc.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveAmcRequest req)
    {
        var amc = await _db.AmcMasters.FindAsync(id);
        if (amc == null) return NotFound(new { message = "AMC not found." });
        amc.Name = req.Name; amc.InstagramUrl = req.InstagramUrl;
        amc.YoutubeChannelId = req.YoutubeChannelId; amc.AutoSyncEnabled = req.AutoSyncEnabled;
        await _db.SaveChangesAsync();
        return Ok(new { message = "AMC updated." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var amc = await _db.AmcMasters.FindAsync(id);
        if (amc == null) return NotFound(new { message = "AMC not found." });
        _db.AmcMasters.Remove(amc);
        await _db.SaveChangesAsync();
        return Ok(new { message = "AMC deleted." });
    }
}

// ─────────────────────────────────────────────────────────────
// ADMIN — ROLES / RBAC
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/roles")]
////[Authorize(Policy = "AdminOnly")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class AdminRolesController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminRolesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var roles = await _db.Roles.Include(r => r.Permissions).ToListAsync();
        var result = roles.Select(r => new RoleDto(r.Id, r.Name,
            r.Permissions.Select(p => new PermissionDto(
                p.Id, p.FeatureArea, p.CanView, p.CanEdit, p.CanDelete, p.CanExport)).ToList()
        )).ToList();
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> SaveRole([FromBody] SaveRoleRequest req)
    {
        var role = await _db.Roles.Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Name == req.Name);

        if (role == null)
        {
            role = new Role { Name = req.Name };
            _db.Roles.Add(role);
            await _db.SaveChangesAsync();
        }
        else
        {
            _db.Permissions.RemoveRange(role.Permissions);
            await _db.SaveChangesAsync();
        }

        foreach (var p in req.Permissions)
        {
            _db.Permissions.Add(new Permission
            {
                RoleId = role.Id,
                FeatureArea = p.FeatureArea,
                CanView = p.CanView,
                CanEdit = p.CanEdit,
                CanDelete = p.CanDelete,
                CanExport = p.CanExport
            });
        }
        await _db.SaveChangesAsync();
        return Ok(new { message = "Role saved.", id = role.Id });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var role = await _db.Roles.FindAsync(id);
        if (role == null) return NotFound(new { message = "Role not found." });
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Role deleted." });
    }
}

// ─────────────────────────────────────────────────────────────
// ADMIN — CONTENT (Upload + Embed + List)
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/content")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "AdminOnly")]
public class AdminContentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly EmailService _email;
    private readonly IConfiguration _cfg;
    public AdminContentController(AppDbContext db, IWebHostEnvironment env, EmailService email, IConfiguration cfg)
    { _db = db; _env = env; _email = email; _cfg = cfg; }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? categoryId, [FromQuery] string? search,
        [FromQuery] string? assetType, [FromQuery] string? source)
    {
        var q = _db.ContentAssets.Include(c => c.Category).Include(c => c.Amc)
            .Where(c => c.IsActive);
        if (categoryId.HasValue) q = q.Where(c => c.CategoryId == categoryId);
        if (!string.IsNullOrEmpty(search)) q = q.Where(c => c.Title.Contains(search));
        if (!string.IsNullOrEmpty(assetType)) q = q.Where(c => c.AssetType == assetType);
        if (!string.IsNullOrEmpty(source)) q = q.Where(c => c.Source == source);

        var list = await q.OrderByDescending(c => c.CreatedAt)
            .Select(c => new ContentAssetDto(c.Id, c.Title, c.AssetType,
                c.CategoryId, c.Category != null ? c.Category.Name : null,
                c.AmcId, c.Amc != null ? c.Amc.Name : null,
                c.FilePath, c.EmbedUrl, c.Source, c.IsActive, c.CreatedAt))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(52_428_800)] // 50 MB
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file, [FromForm] string title,
        [FromForm] int? categoryId, [FromForm] int? amcId)
    {
        var uploadsPath = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads");
        Directory.CreateDirectory(uploadsPath);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid()}{ext}";
        var fullPath = Path.Combine(uploadsPath, fileName);

        using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream);

        var assetType = ext switch
        {
            ".mp4" or ".mov" or ".avi" => "video",
            ".pdf" => "pdf",
            _ => "image"
        };

        var asset = new ContentAsset
        {
            Title = title,
            AssetType = assetType,
            CategoryId = categoryId,
            AmcId = amcId,
            FilePath = $"/uploads/{fileName}",
            Source = "manual"
        };
        _db.ContentAssets.Add(asset);
        await _db.SaveChangesAsync();

        // Notify all approved active partners
        var appUrl = _cfg["AppUrl"] ?? "https://saarthi.regnumdigital.co.in";   //http://localhost:5000
        var catName = asset.CategoryId.HasValue
            ? (await _db.Categories.FindAsync(asset.CategoryId))?.Name ?? "General"
            : "General";
        var partners = await _db.Partners.Where(p => p.IsActive && p.ApprovalStatus == "approved").ToListAsync();
        _ = Task.Run(async () => {
            foreach (var p in partners)
                await _email.SendNewContentEmailAsync(p.Email, p.FullName, asset.Title, asset.AssetType, catName, appUrl);
        });

        return Ok(new { message = "File uploaded.", id = asset.Id, path = asset.FilePath });
    }

    [HttpPost("embed")]
    public async Task<IActionResult> Embed([FromBody] SaveContentRequest req)
    {
        var asset = new ContentAsset
        {
            Title = req.Title,
            AssetType = "embed",
            CategoryId = req.CategoryId,
            AmcId = req.AmcId,
            EmbedUrl = req.EmbedUrl,
            Source = "manual"
        };
        _db.ContentAssets.Add(asset);
        await _db.SaveChangesAsync();
        var appUrl2 = _cfg["AppUrl"] ?? "https://saarthi.regnumdigital.co.in";
        var partners2 = await _db.Partners.Where(p => p.IsActive && p.ApprovalStatus == "approved").ToListAsync();
        _ = Task.Run(async () => {
            foreach (var p in partners2)
                await _email.SendNewContentEmailAsync(p.Email, p.FullName, asset.Title, "embed", "Embed", appUrl2);
        });
        return Ok(new { message = "Embed saved.", id = asset.Id });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var asset = await _db.ContentAssets.FindAsync(id);
        if (asset == null) return NotFound(new { message = "Content not found." });
        asset.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Content removed from portal." });
    }
}

// ─────────────────────────────────────────────────────────────
// ADMIN — LMS COURSE BUILDER
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/lms")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "AdminOnly")]
public class AdminLmsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminLmsController(AppDbContext db) => _db = db;

    [HttpGet("courses")]
    public async Task<IActionResult> GetCourses()
    {
        var list = await _db.LmsCourses.Include(c => c.Lessons)
            .Select(c => new CourseDto(c.Id, c.Title, c.Description,
                c.Lessons.OrderBy(l => l.SortOrder)
                    .Select(l => new LessonDto(l.Id, l.Title, l.LessonType, l.SortOrder, null))
                    .ToList()))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("courses")]
    public async Task<IActionResult> CreateCourse([FromBody] SaveCourseRequest req)
    {
        var course = new LmsCourse { Title = req.Title, Description = req.Description };
        _db.LmsCourses.Add(course);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Course created.", id = course.Id });
    }

    [HttpDelete("courses/{id}")]
    public async Task<IActionResult> DeleteCourse(int id)
    {
        var c = await _db.LmsCourses.FindAsync(id);
        if (c == null) return NotFound();
        c.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Course deactivated." });
    }

    [HttpPost("lessons")]
    public async Task<IActionResult> AddLesson([FromBody] SaveLessonRequest req)
    {
        var lesson = new LmsLesson
        {
            CourseId = req.CourseId,
            Title = req.Title,
            LessonType = req.LessonType,
            SortOrder = req.SortOrder,
            AssetId = req.AssetId
        };
        _db.LmsLessons.Add(lesson);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Lesson added.", id = lesson.Id });
    }

    [HttpDelete("lessons/{id}")]
    public async Task<IActionResult> DeleteLesson(int id)
    {
        var l = await _db.LmsLessons.FindAsync(id);
        if (l == null) return NotFound();
        _db.LmsLessons.Remove(l);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Lesson deleted." });
    }
}
