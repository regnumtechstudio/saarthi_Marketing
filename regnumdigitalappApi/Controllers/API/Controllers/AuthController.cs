using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegnumDigital.API.Controllers;
using RegnumDigital.API.Data;
using RegnumDigital.API.DTOs;
using RegnumDigital.API.Models;
using RegnumDigital.API.Services;
using static modelObject.DTOs_additions;

namespace RegnumDigital.API.Controllers;

// ─────────────────────────────────────────────────────────────
// ADMIN AUTH
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin/auth")]
public class AdminAuthController : ControllerBase
{
    private readonly AppDbContext _db;
    
    private readonly JwtService _jwt;
    private readonly OtpService _otp;
    private readonly ILogger<AdminAuthController> _log;

    public AdminAuthController(AppDbContext db, JwtService jwt, OtpService otp, ILogger<AdminAuthController> log)
    { _db = db; _jwt = jwt; _otp = otp; _log = log; }

    /// <summary>Step 1: Login with email+password → OTP sent to console</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var admin = await _db.AdminUsers
            .FirstOrDefaultAsync(a => a.Email == req.Email && a.IsActive);

        //if (admin == null || !BCrypt.Net.BCrypt.Verify(req.Password, admin.Password))
        //    return Unauthorized(new { message = "Invalid email or password." });

        if (admin == null)
            return Unauthorized(new { message = "Invalid email or password." });

        await _otp.GenerateAndSaveAsync(admin.Email, "admin");
        return Ok(new { message = "OTP sent. Check server console.", email = admin.Email });
    }

    /// <summary>Step 2: Verify OTP → Returns JWT token</summary>
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyRequest req)
    {
        var valid = await _otp.VerifyAsync(req.Identifier, req.OtpCode, "admin");
        if (!valid) return BadRequest(new { message = "Invalid or expired OTP." });

        var admin = await _db.AdminUsers.FirstOrDefaultAsync(a => a.Email == req.Identifier);
        if (admin == null) return NotFound();

        var token = _jwt.GenerateToken(admin.Id, admin.Email, admin.Name, "admin");
        return Ok(new AuthResponse(token, admin.Name, admin.Email, "admin"));
    }

    [HttpPost("send-otp")]
    public async Task<IActionResult> sendotp([FromBody] TestEmailDto dto)
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
                Subject = "✅ Regnum Digital — one time password",
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
// PARTNER AUTH
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/partner/auth")]
public class PartnerAuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;
    private readonly OtpService _otp;
    private readonly ILogger<PartnerAuthController> _log;

    public PartnerAuthController(AppDbContext db, JwtService jwt, OtpService otp, ILogger<PartnerAuthController> log)
    { _db = db; _jwt = jwt; _otp = otp; _log = log; }

    /// <summary>Step 1: Partner login → OTP sent</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var partner = await _db.Partners
            .FirstOrDefaultAsync(p => p.Email == req.Email);

        //if (partner == null || partner.Password == null
        //||!BCrypt.Net.BCrypt.Verify(req.Password, partner.Password))
        //return Unauthorized(new { message = "Invalid email or password." });

    if (partner == null || partner.Password == null)
            return Unauthorized(new { message = "Invalid email or password." });

        if (partner.ApprovalStatus == "pending")
            return Unauthorized(new { message = "Your account is pending admin approval. You will be notified by email once approved." });
        if (partner.ApprovalStatus == "rejected")
            return Unauthorized(new { message = $"Your account has been rejected. Reason: {partner.RejectionReason ?? "Contact admin."}" });
        if (!partner.IsActive)
            return Unauthorized(new { message = "Your account has been deactivated. Contact admin." });

        await _otp.GenerateAndSaveAsync(partner.Email, "partner", partner.FullName);
        return Ok(new { message = "OTP sent to your email.", email = partner.Email });
    }

    [HttpPost("resend-otp")]
    public async Task<IActionResult> ResendOtp([FromBody] ForgotPasswordRequest req)
    {
        var partner = await _db.Partners.FirstOrDefaultAsync(p => p.Email == req.Email && p.IsActive);
        if (partner == null) return BadRequest(new { message = "Email not found." });
        await _otp.GenerateAndSaveAsync(partner.Email, "partner", partner.FullName);
        return Ok(new { message = "New OTP sent." });
    }

    /// <summary>New partner registration → OTP sent</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] CreatePartnerRequest req)
    {
        if (await _db.Partners.AnyAsync(p => p.Email == req.Email))
            return Conflict(new { message = "Email already registered." });

        var defaultRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Basic Free");
        var defaultPlan = await _db.Plans.FirstOrDefaultAsync(p => p.Name == "Free Lifetime");

        var partner = new Partner
        {
            FullName = req.FullName,
            Email = req.Email,
            Mobile = req.Mobile,
            ArnNumber = req.ArnNumber,
            BusinessName = req.BusinessName,
            RoleId = req.RoleId ?? defaultRole?.Id,
            PlanId = req.PlanId ?? defaultPlan?.Id,
            Password = BCrypt.Net.BCrypt.HashPassword(req.Password ?? "Regnum@123"),
            IsActive = false,
            ApprovalStatus = "pending"
        };
        _db.Partners.Add(partner);
        await _db.SaveChangesAsync();

        await _otp.GenerateAndSaveAsync(partner.Email, "partner");
        return Ok(new { message = "Registration submitted! An admin will review and approve your account. You will receive an email once approved." });
    }

    /// <summary>Step 2: Verify OTP → Returns JWT token</summary>
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyRequest req)
    {
        var valid = await _otp.VerifyAsync(req.Identifier, req.OtpCode, "partner");
        if (!valid) return BadRequest(new { message = "Invalid or expired OTP." });

        var partner = await _db.Partners.FirstOrDefaultAsync(p => p.Email == req.Identifier);
        if (partner == null) return NotFound();

        var token = _jwt.GenerateToken(partner.Id, partner.Email, partner.FullName, "partner");
        return Ok(new AuthResponse(token, partner.FullName, partner.Email, "partner"));
    }

    [HttpPost("send-otp")]
    public async Task<IActionResult> sendotp([FromBody] TestEmailDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { message = "Target email address is required." });

        var cfg = await _db.EmailConfigs.FirstOrDefaultAsync();
        if (cfg == null || string.IsNullOrWhiteSpace(cfg.SmtpHost))
            return BadRequest(new { message = "No SMTP configuration saved. Please save settings first." });

        try
        {
            var emailtemplate = EmailService.DefaultTemplates()
                                            .Where(r=> r.TemplateKey == "otp_login")
                                            .FirstOrDefault();

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
                Body = emailtemplate.BodyHtml.ToString().Replace("{{OTP_CODE}}", "\n")
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

