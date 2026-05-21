using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegnumDigital.API.Data;
using RegnumDigital.API.DTOs;
using RegnumDigital.API.Models;
using RegnumDigital.API.Services;
using static modelObject.DTOs_additions;
using EmailConfigDto = modelObject.DTOs_additions.EmailConfigDto;

namespace RegnumDigital.API.Controllers;

// ─────────────────────────────────────────────────────────────
// SHARED — FORGOT PASSWORD (no auth)
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/auth/forgot-password")]
public class ForgotPasswordController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly IConfiguration _cfg;

    public ForgotPasswordController(AppDbContext db, EmailService email, IConfiguration cfg)
    { _db = db; _email = email; _cfg = cfg; }

    [HttpPost]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        // Try partner first, then admin
        string? name     = null;
        string  userType = "partner";

        var partner = await _db.Partners.FirstOrDefaultAsync(p => p.Email == req.Email && p.IsActive);
        if (partner != null) { name = partner.FullName; userType = "partner"; }
        else
        {
            var admin = await _db.AdminUsers.FirstOrDefaultAsync(a => a.Email == req.Email && a.IsActive);
            if (admin != null) { name = admin.Name; userType = "admin"; }
        }

        // Always return OK to prevent enumeration
        if (name == null) return Ok(new { message = "If this email is registered, a reset link has been sent." });

        // Invalidate existing tokens
        var oldTokens = _db.PasswordResetTokens
            .Where(t => t.Identifier == req.Email && t.UserType == userType && !t.IsUsed);
        foreach (var t in oldTokens) t.IsUsed = true;

        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Identifier = req.Email,
            UserType   = userType,
            Token      = token,
            ExpiresAt  = DateTime.UtcNow.AddMinutes(30)
        });
        await _db.SaveChangesAsync();

        var appUrl   = _cfg["AppUrl"] ?? "https://saarthi.regnumdigital.co.in";
        var resetUrl = $"{appUrl}/reset-password?token={token}&type={userType}";

        // Log to console for dev
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n[RESET LINK] {resetUrl}\n");
        Console.ResetColor();

        _ = Task.Run(() => _email.SendPasswordResetEmailAsync(req.Email, name, resetUrl));
        return Ok(new { message = "If this email is registered, a reset link has been sent." });
    }

    [HttpPost("reset")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        var tokenRecord = await _db.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.Token == req.Token && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow);
        if (tokenRecord == null) return BadRequest(new { message = "Invalid or expired reset link." });

        var hashed = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);

        if (tokenRecord.UserType == "partner")
        {
            var p = await _db.Partners.FirstOrDefaultAsync(x => x.Email == tokenRecord.Identifier);
            if (p == null) return NotFound();
            p.Password = hashed;
        }
        else
        {
            var a = await _db.AdminUsers.FirstOrDefaultAsync(x => x.Email == tokenRecord.Identifier);
            if (a == null) return NotFound();
            a.Password = hashed;
        }

        tokenRecord.IsUsed = true;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Password reset successfully. You may now log in." });
    }
}

// ─────────────────────────────────────────────────────────────
// PARTNER — CHANGE PASSWORD (authenticated)
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/partner/auth/change-password")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "PartnerOnly")]
public class PartnerChangePasswordController : ControllerBase
{
    private readonly AppDbContext _db;
    private int Pid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public PartnerChangePasswordController(AppDbContext db) => _db = db;

    [HttpPost]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var p = await _db.Partners.FindAsync(Pid);
        if (p == null || p.Password == null) return NotFound();
        if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, p.Password))
            return BadRequest(new { message = "Current password is incorrect." });
        p.Password = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Password changed successfully." });
    }
}

// ─────────────────────────────────────────────────────────────
// ADMIN — CHANGE PASSWORD (authenticated)
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/auth/change-password")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "AdminOnly")]
public class AdminChangePasswordController : ControllerBase
{
    private readonly AppDbContext _db;
    private int Aid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public AdminChangePasswordController(AppDbContext db) => _db = db;

    [HttpPost]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var a = await _db.AdminUsers.FindAsync(Aid);
        if (a == null) return NotFound();
        if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, a.Password))
            return BadRequest(new { message = "Current password is incorrect." });
        a.Password = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Password changed successfully." });
    }
}

// ─────────────────────────────────────────────────────────────
// ADMIN — PARTNER APPROVAL
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/partners/{id}/approval")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "AdminOnly")]
public class PartnerApprovalController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly IConfiguration _cfg;

    public PartnerApprovalController(AppDbContext db, EmailService email, IConfiguration cfg)
    { _db = db; _email = email; _cfg = cfg; }

    [HttpPost]
    public async Task<IActionResult> SetApproval(int id, [FromBody] PartnerApprovalRequest req)
    {
        var partner = await _db.Partners.FindAsync(id);
        if (partner == null) return NotFound(new { message = "Partner not found." });

        if (req.Action == "approve")
        {
            partner.ApprovalStatus = "approved";
            partner.IsActive       = true;
            partner.RejectionReason = null;
            await _db.SaveChangesAsync();

            var appUrl = _cfg["AppUrl"] ?? "https://saarthi.regnumdigital.co.in";
            _ = Task.Run(() => _email.SendWelcomeEmailAsync(partner.Email, partner.FullName, appUrl));
            return Ok(new { message = "Partner approved and welcome email sent." });
        }
        else if (req.Action == "reject")
        {
            partner.ApprovalStatus  = "rejected";
            partner.IsActive        = false;
            partner.RejectionReason = req.RejectionReason;
            await _db.SaveChangesAsync();
            return Ok(new { message = "Partner rejected." });
        }

        return BadRequest(new { message = "Action must be 'approve' or 'reject'." });
    }
}

// ─────────────────────────────────────────────────────────────
// ADMIN — PENDING PARTNERS LIST
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/partners/pending")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "AdminOnly")]
public class PendingPartnersController : ControllerBase
{
    private readonly AppDbContext _db;
    public PendingPartnersController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetPending()
    {
        var list = await _db.Partners
            .Include(p => p.Role).Include(p => p.Plan)
            .Where(p => p.ApprovalStatus == "pending")
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PartnerDto(p.Id, p.FullName, p.Email, p.Mobile,
                p.ArnNumber, p.BusinessName, p.Role != null ? p.Role.Name : null,
                p.Plan != null ? p.Plan.Name : null, p.IsActive, p.ApprovalStatus, p.CreatedAt))
            .ToListAsync();
        return Ok(list);
    }
}

// ─────────────────────────────────────────────────────────────
// ADMIN — EMAIL CONFIG
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/email-config")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "AdminOnly")]
public class EmailConfigController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmailConfigController(AppDbContext db) => _db = db;
    private readonly ILogger<EmailConfigController> _log;
    //[HttpGet]
    //public async Task<IActionResult> Get()
    //{
    //    var cfg = await _db.EmailConfigs.FirstOrDefaultAsync();

    //    if (cfg == null) return Ok(new EmailConfigDto(0, "smtp.gmail.com", 587, "", "", "Regnum Digital", "", true));
    //    // Mask password in response
    //    return Ok(new EmailConfigDto(cfg.Id, cfg.SmtpHost, cfg.SmtpPort, cfg.SmtpUser,
    //        string.IsNullOrEmpty(cfg.SmtpPassword) ? "" : "••••••••",
    //        cfg.FromName, cfg.FromEmail, cfg.IsActive));
    //}

    //[HttpPost]
    //public async Task<IActionResult> Save([FromBody] EmailConfigDto req)
    //{
    //    var cfg = await _db.EmailConfigs.FirstOrDefaultAsync();
    //    if (cfg == null) { cfg = new EmailConfig(); _db.EmailConfigs.Add(cfg); }
    //    cfg.SmtpHost  = req.SmtpHost;
    //    cfg.SmtpPort  = req.SmtpPort;
    //    cfg.SmtpUser  = req.SmtpUser;
    //    cfg.FromName  = req.FromName;
    //    cfg.FromEmail = req.FromEmail;
    //    cfg.IsActive  = req.IsActive;
    //    // Only update password if a real value (not masked) is sent
    //    if (!string.IsNullOrEmpty(req.SmtpPassword) && req.SmtpPassword != "••••••••")
    //        cfg.SmtpPassword = req.SmtpPassword;
    //    cfg.UpdatedAt = DateTime.UtcNow;
    //    await _db.SaveChangesAsync();
    //    return Ok(new { message = "Email configuration saved." });
    //}

    //[HttpPost("test")]
    //public async Task<IActionResult> TestEmail([FromBody] ForgotPasswordRequest req, [FromServices] EmailService emailSvc)
    //{
    //    var ok = await emailSvc.SendAsync(req.Email, "Test", "Regnum Digital – SMTP Test",
    //        "<h2>SMTP is working correctly! ✅</h2><p>Your email configuration is set up properly.</p>");
    //    return ok ? Ok(new { message = "Test email sent successfully!" })
    //              : BadRequest(new { message = "Failed to send test email. Check SMTP settings." });
    //}


    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var cfg = await _db.EmailConfigs.FirstOrDefaultAsync() ?? new EmailConfig();
        return Ok(new EmailConfigDto
        {
            Id = cfg.Id,
            SmtpHost = cfg.SmtpHost,
            SmtpPort = cfg.SmtpPort,
            SmtpUser = cfg.SmtpUser,
            SmtpPassword = cfg.SmtpPassword,
            FromName = cfg.FromName,
            FromEmail = cfg.FromEmail,
            IsActive = cfg.IsActive
        });
    }

    // POST /api/admin/email-config  (upsert)
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] EmailConfigDto dto)
    {
        var cfg = await _db.EmailConfigs.FirstOrDefaultAsync();
        if (cfg == null) { cfg = new EmailConfig(); _db.EmailConfigs.Add(cfg); }

        cfg.SmtpHost = dto.SmtpHost?.Trim();
        cfg.SmtpPort = dto.SmtpPort > 0 ? dto.SmtpPort : 587;
        cfg.SmtpUser = dto.SmtpUser?.Trim();
        cfg.SmtpPassword = dto.SmtpPassword;
        cfg.FromName = dto.FromName?.Trim();
        cfg.FromEmail = dto.FromEmail?.Trim();
        cfg.IsActive = true;
        cfg.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { message = "Email configuration saved." });
    }

    // POST /api/admin/email-config/test
    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] TestEmailDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { message = "Target email address is required." });

        var cfg = await _db.EmailConfigs.FirstOrDefaultAsync();
        if (cfg == null || string.IsNullOrWhiteSpace(cfg.SmtpHost))
            return BadRequest(new { message = "No SMTP configuration saved. Please save settings first." });

        try
        {
            using var client = new System.Net.Mail.SmtpClient(cfg.SmtpHost, cfg.SmtpPort)
            {
                EnableSsl = true,
                DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new System.Net.NetworkCredential(cfg.SmtpUser, cfg.SmtpPassword),
                Timeout = 15000
            };

            var fromAddr = new System.Net.Mail.MailAddress(
                cfg.FromEmail ?? cfg.SmtpUser ?? "noreply@regnum.in",
                cfg.FromName ?? "Regnum Digital"
            );
            var toAddr = new System.Net.Mail.MailAddress(dto.Email.Trim());

            using var msg = new System.Net.Mail.MailMessage(fromAddr, toAddr)
            {
                Subject = "✅ Regnum Digital — SMTP Test Email",
                IsBodyHtml = true,
                Body = $@"
                    <div style=""font-family:sans-serif;max-width:480px;margin:0 auto;padding:32px 24px;"">
                      <h2 style=""color:#6d4aff;margin-bottom:8px;"">SMTP Configuration Working ✅</h2>
                      <p style=""color:#444;line-height:1.6;"">
                        This test email confirms your SMTP settings are correctly configured in
                        <strong>Regnum Digital Admin Panel</strong>.
                      </p>
                      <table style=""width:100%;border-collapse:collapse;margin:20px 0;font-size:13px;"">
                        <tr><td style=""padding:6px 0;color:#888;"">SMTP Host</td><td style=""color:#222;font-weight:600;"">{cfg.SmtpHost}:{cfg.SmtpPort}</td></tr>
                        <tr><td style=""padding:6px 0;color:#888;"">From</td><td style=""color:#222;font-weight:600;"">{fromAddr.DisplayName} &lt;{fromAddr.Address}&gt;</td></tr>
                        <tr><td style=""padding:6px 0;color:#888;"">Sent to</td><td style=""color:#222;font-weight:600;"">{dto.Email}</td></tr>
                        <tr><td style=""padding:6px 0;color:#888;"">Time</td><td style=""color:#222;font-weight:600;"">{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</td></tr>
                      </table>
                      <p style=""color:#aaa;font-size:12px;"">Regnum Digital · Automated test message</p>
                    </div>"
            };

            await client.SendMailAsync(msg);
            _log.LogInformation("Test email sent to {Email} via {Host}", dto.Email, cfg.SmtpHost);
            return Ok(new { message = $"Test email delivered to {dto.Email} successfully." });
        }
        catch (System.Net.Mail.SmtpException ex)
        {
            _log.LogWarning("SMTP test failed: {Msg}", ex.Message);
            return StatusCode(500, new { message = $"SMTP error ({(int)ex.StatusCode}): {ex.Message}" });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Email test unexpected error");
            return StatusCode(500, new { message = ex.Message });
        }
    }


}

// ─────────────────────────────────────────────────────────────
// ADMIN — EMAIL TEMPLATES
// ─────────────────────────────────────────────────────────────
//[ApiController]
//[Route("api/admin/email-templates")]
////[Authorize(Policy = "AdminOnly")]
//public class EmailTemplatesController : ControllerBase
//{
//    private readonly AppDbContext _db;
//    public EmailTemplatesController(AppDbContext db) => _db = db;

//    [HttpGet]
//    public async Task<IActionResult> GetAll()
//    {
//        var list = await _db.EmailTemplates
//            .Select(t => new EmailTemplateDto(t.Id, t.TemplateKey, t.Subject, t.BodyHtml, t.IsActive))
//            .ToListAsync();
//        return Ok(list);
//    }

//    [HttpPut("{key}")]
//    public async Task<IActionResult> Update(string key, [FromBody] SaveEmailTemplateRequest req)
//    {
//        var tmpl = await _db.EmailTemplates.FirstOrDefaultAsync(t => t.TemplateKey == key);
//        if (tmpl == null) return NotFound(new { message = "Template not found." });
//        tmpl.Subject   = req.Subject;
//        tmpl.BodyHtml  = req.BodyHtml;
//        tmpl.UpdatedAt = DateTime.UtcNow;
//        await _db.SaveChangesAsync();
//        return Ok(new { message = "Template updated." });
//    }
//}

[ApiController]
[Route("api/admin/email-templates")]
[Authorize(AuthenticationSchemes = "Bearer")]
//[Authorize(Policy = "AdminOnly")]
public class EmailTemplatesController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmailTemplatesController(AppDbContext db) { _db = db; }

    // GET /api/admin/email-templates
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var templates = await _db.EmailTemplates.ToListAsync();

        // Seed defaults if empty
        if (!templates.Any())
        {
            var defaults = DefaultTemplates();
            _db.EmailTemplates.AddRange(defaults);
            await _db.SaveChangesAsync();
            templates = defaults;
        }
        return Ok(templates.Select(t => new { t.Id, templateKey = t.TemplateKey, t.Subject, bodyHtml = t.BodyHtml }));
    }

    // PUT /api/admin/email-templates/{key}
    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] SaveEmailTemplateDto dto)
    {
        var t = await _db.EmailTemplates.FirstOrDefaultAsync(x => x.TemplateKey == key);
        if (t == null) return NotFound(new { message = "Template not found." });
        t.Subject = dto.Subject;
        t.BodyHtml = dto.BodyHtml;
        t.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Template saved." });
    }

    private static List<EmailTemplate> DefaultTemplates() => new()
    {
        new EmailTemplate { TemplateKey = "otp_login",
            Subject  = "Your Regnum Digital OTP: {{OTP_CODE}}",
            BodyHtml = "<p>Your OTP is <strong>{{OTP_CODE}}</strong>. Valid for 10 minutes.</p>" },
        new EmailTemplate { TemplateKey = "partner_welcome",
            Subject  = "Welcome to Regnum Digital, {{PARTNER_NAME}}!",
            BodyHtml = "<p>Hi {{PARTNER_NAME}}, welcome aboard! Login at {{APP_URL}} with {{PARTNER_EMAIL}}.</p>" },
        new EmailTemplate { TemplateKey = "new_content",
            Subject  = "New {{CONTENT_TYPE}} available: {{CONTENT_TITLE}}",
            BodyHtml = "<p>Hi {{PARTNER_NAME}}, new content <strong>{{CONTENT_TITLE}}</strong> in {{CONTENT_CATEGORY}} is live. Visit {{APP_URL}}.</p>" },
        new EmailTemplate { TemplateKey = "password_reset",
            Subject  = "Reset your Regnum Digital password",
            BodyHtml = "<p>Click <a href=\"{{RESET_URL}}\">here</a> to reset your password. Link expires in 1 hour.</p>" },
    };
}
