using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegnumDigital.API.Data;
using RegnumDigital.API.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using static modelObject.DTOs_additions;
//using static modelObject.Models_additions;


namespace regnumdigitalappApi.Controllers.API
{
    [ApiController]
    [Route("api/admin/fcm")]
   // [Authorize(Policy = "AdminOnly")]
    public class FcmAdminController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<FcmAdminController> _log;
        public FcmAdminController(AppDbContext db, ILogger<FcmAdminController> log)
        { _db = db; _log = log; }

        // GET /api/admin/fcm/config
        [HttpGet("config")]
        public async Task<IActionResult> GetConfig()
        {
            var cfg = await _db.FcmConfigs.FirstOrDefaultAsync();
            if (cfg == null) return Ok(new FcmConfigDto());
            return Ok(new FcmConfigDto
            {
                ProjectId = cfg.ProjectId,
                ServerKey = string.IsNullOrEmpty(cfg.ServerKey) ? "" : "••••••••",
                VapidKey = string.IsNullOrEmpty(cfg.VapidKey) ? "" : "••••••••",
                ApiKey = cfg.ApiKey,
                AuthDomain = cfg.AuthDomain,
                AppId = cfg.AppId,
            });
        }

        // POST /api/admin/fcm/config
        [HttpPost("config")]
        public async Task<IActionResult> SaveConfig([FromBody] FcmConfigDto req)
        {
            var cfg = await _db.FcmConfigs.FirstOrDefaultAsync();
            if (cfg == null) { cfg = new FcmConfig(); _db.FcmConfigs.Add(cfg); }
            cfg.ProjectId = req.ProjectId ?? cfg.ProjectId;
            cfg.ApiKey = req.ApiKey ?? cfg.ApiKey;
            cfg.AuthDomain = req.AuthDomain ?? cfg.AuthDomain;
            cfg.AppId = req.AppId ?? cfg.AppId;
            if (!string.IsNullOrEmpty(req.ServerKey) && req.ServerKey != "••••••••")
                cfg.ServerKey = req.ServerKey;
            if (!string.IsNullOrEmpty(req.VapidKey) && req.VapidKey != "••••••••")
                cfg.VapidKey = req.VapidKey;
            await _db.SaveChangesAsync();
            return Ok(new { message = "FCM config saved." });
        }

        // POST /api/admin/fcm/test
        [HttpPost("test")]
        public async Task<IActionResult> TestConfig()
        {
            var cfg = await _db.FcmConfigs.FirstOrDefaultAsync();
            if (cfg == null || string.IsNullOrEmpty(cfg.ServerKey))
                return BadRequest(new { message = "FCM not configured. Save config first." });
            var count = await _db.FcmTokens.CountAsync(t => t.IsActive);
            return Ok(new { message = "Firebase connected!", subscriberCount = count });
        }

        // GET /api/admin/fcm/stats
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var today = DateTime.UtcNow.Date;
            var totalSubs = await _db.FcmTokens.CountAsync(t => t.IsActive);
            var partnerDevs = await _db.FcmTokens.Where(t => t.IsActive).Select(t => t.PartnerId).Distinct().CountAsync();
            var sentToday = await _db.PushNotifications.CountAsync(n => n.CreatedAt >= today);
            var allNotifs = await _db.PushNotifications.ToListAsync();
            double openRate = allNotifs.Any() && allNotifs.Sum(n => n.SentCount) > 0
                ? Math.Round(allNotifs.Sum(n => n.OpenedCount) * 100.0 / allNotifs.Sum(n => n.SentCount), 1)
                : 0;
            return Ok(new { totalSubscribers = totalSubs, partnerDevices = partnerDevs, sentToday, openRate });
        }

        // GET /api/admin/fcm/history
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory()
        {
            var list = await _db.PushNotifications
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .Select(n => new {
                    n.Id,
                    n.Title,
                    n.Body,
                    n.Audience,
                    n.SentCount,
                    n.OpenedCount,
                    n.CreatedAt
                }).ToListAsync();
            return Ok(list);
        }

        // POST /api/admin/fcm/send
        [HttpPost("send")]
        public async Task<IActionResult> SendNotification([FromBody] SendPushDto req)
        {
            if (string.IsNullOrEmpty(req.Title) || string.IsNullOrEmpty(req.Body))
                return BadRequest(new { message = "Title and body required." });

            var cfg = await _db.FcmConfigs.FirstOrDefaultAsync();
            if (cfg == null || string.IsNullOrEmpty(cfg.ServerKey))
                return BadRequest(new { message = "FCM Server Key not configured." });

            // Get target tokens
            var query = _db.FcmTokens.Where(t => t.IsActive);
            if (req.Audience == "specific" && req.PartnerId.HasValue)
                query = query.Where(t => t.PartnerId == req.PartnerId.Value);
            else if (req.Audience == "role" && req.RoleId.HasValue)
                query = query.Where(t => t.Partner.RoleId == req.RoleId.Value);
            var tokens = await query.Select(t => t.Token).Distinct().ToListAsync();

            if (!tokens.Any())
                return BadRequest(new { message = "No active subscriber tokens found for this audience." });

            // Fire-and-forget FCM send
            int successCount = 0;
            _ = Task.Run(async () =>
            {
                successCount = await SendFcmBatch(cfg.ServerKey, tokens, req);
                var record = new PushNotification
                {
                    Title = req.Title,
                    Body = req.Body,
                    ClickUrl = req.ClickUrl,
                    Audience = req.Audience ?? "all",
                    PartnerId = req.PartnerId,
                    RoleId = req.RoleId,
                    SentCount = successCount,
                };
                _db.PushNotifications.Add(record);
                await _db.SaveChangesAsync();
            });

            return Ok(new { message = $"Sending to {tokens.Count} devices", successCount = tokens.Count });
        }

        // POST /api/admin/fcm/send-test
        [HttpPost("send-test")]
        public async Task<IActionResult> SendTest([FromBody] SendTestPushDto req)
        {
            var cfg = await _db.FcmConfigs.FirstOrDefaultAsync();
            if (cfg == null || string.IsNullOrEmpty(cfg.ServerKey))
                return BadRequest(new { message = "FCM not configured." });
            // Find admin's last token (if they're also a partner) or first token
            var token = await _db.FcmTokens.Where(t => t.IsActive)
                .OrderByDescending(t => t.UpdatedAt).Select(t => t.Token).FirstOrDefaultAsync();
            if (token == null) return BadRequest(new { message = "No device tokens registered yet. Open partner portal on your device first." });
            var testReq = new SendPushDto
            {
                Title = "🔔 Test Notification",
                Body = "FCM is working! Regnum Digital push notifications are active.",
                Audience = "specific"
            };
            await SendFcmBatch(cfg.ServerKey, new List<string> { token }, testReq);
            return Ok(new { message = "Test notification sent!" });
        }

        // ── FCM HTTP v1 send helper ──────────────────────────────
        private static async Task<int> SendFcmBatch(string serverKey, List<string> tokens, SendPushDto req)
        {
            int success = 0;
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("key", $"={serverKey}");
            // Send in batches of 500
            var batches = tokens.Chunk(500);
            foreach (var batch in batches)
            {
                var payload = new
                {
                    registration_ids = batch,
                    notification = new { title = req.Title, body = req.Body, icon = req.IconUrl ?? "/favicon.ico" },
                    data = new { title = req.Title, body = req.Body, clickUrl = req.ClickUrl ?? "/" }
                };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                try
                {
                    var resp = await http.PostAsync("https://fcm.googleapis.com/fcm/send", content);
                    if (resp.IsSuccessStatusCode) success += batch.Count();
                }
                catch { /* log silently */ }
            }
            return success;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // FCM — Partner Token Registration  /api/partner/fcm
    // ════════════════════════════════════════════════════════════════
    [ApiController]
    [Route("api/partner/fcm")]
   // [Authorize(Policy = "PartnerOnly")]
    public class FcmPartnerController : ControllerBase
    {
        private readonly AppDbContext _db;
        public FcmPartnerController(AppDbContext db) => _db = db;

        // GET /api/partner/fcm/config  — Returns public-safe FCM config for client init
        [HttpGet("config")]
        public async Task<IActionResult> GetPublicConfig()
        {
            var cfg = await _db.FcmConfigs.FirstOrDefaultAsync(c => c.IsActive);
            if (cfg == null || string.IsNullOrEmpty(cfg.ApiKey))
                return Ok(new { configured = false });
            return Ok(new
            {
                configured = true,
                projectId = cfg.ProjectId,
                apiKey = cfg.ApiKey,
                authDomain = cfg.AuthDomain,
                appId = cfg.AppId,
                vapidKey = cfg.VapidKey,
            });
        }

        // POST /api/partner/fcm/token  — Save or update device FCM token
        [HttpPost("token")]
        public async Task<IActionResult> RegisterToken([FromBody] RegisterFcmTokenDto req)
        {
            var partnerId = int.Parse(User.FindFirst("sub")?.Value ?? "0");
            if (partnerId == 0) return Unauthorized();

            // Deactivate old tokens for this device (upsert by token value)
            var existing = await _db.FcmTokens.FirstOrDefaultAsync(t => t.Token == req.Token);
            if (existing != null)
            {
                existing.PartnerId = partnerId;
                existing.IsActive = true;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _db.FcmTokens.Add(new FcmToken
                {
                    PartnerId = partnerId,
                    Token = req.Token,
                    Platform = req.Platform ?? "web",
                    IsActive = true,
                });
            }
            await _db.SaveChangesAsync();
            return Ok(new { message = "Token registered." });
        }
    }

    // ════════════════════════════════════════════════════════════════
    // BACK OFFICE USERS  —  /api/admin/backoffice/users
    // ════════════════════════════════════════════════════════════════
    [ApiController]
    [Route("api/admin/backoffice/users")]
   // [Authorize(Policy = "AdminOnly")]
    public class BackOfficeUserController : ControllerBase
    {
        private readonly AppDbContext _db;
        public BackOfficeUserController(AppDbContext db) => _db = db;

        // GET /api/admin/backoffice/users
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var users = await _db.BackOfficeUsers
                .Include(u => u.Permissions)
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new
                {
                    u.Id,
                    u.Name,
                    u.Email,
                    u.Mobile,
                    u.Role,
                    u.RoleName,
                    u.IsActive,
                    u.CreatedAt,
                    Modules = u.Permissions.Select(p => p.Module).ToList()
                }).ToListAsync();
            return Ok(users);
        }

        // POST /api/admin/backoffice/users
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveBoUserDto req)
        {
            if (await _db.BackOfficeUsers.AnyAsync(u => u.Email == req.Email))
                return Conflict(new { message = "Email already exists." });
            if (string.IsNullOrEmpty(req.Password) || req.Password.Length < 8)
                return BadRequest(new { message = "Password must be at least 8 characters." });

            var adminId = int.Parse(User.FindFirst("sub")?.Value ?? "0");
            var user = new BackOfficeUser
            {
                Name = req.Name,
                Email = req.Email,
                Mobile = req.Mobile,
                Password = BCrypt.Net.BCrypt.HashPassword(req.Password),
                Role = req.Role ?? "custom",
                RoleName = GetRoleDisplayName(req.Role),
                IsActive = true,
                CreatedBy = adminId,
            };
            _db.BackOfficeUsers.Add(user);
            await _db.SaveChangesAsync();

            // Save module permissions
            foreach (var mod in req.Modules ?? new List<string>())
                _db.BoPermissions.Add(new BoPermission { UserId = user.Id, Module = mod });
            await _db.SaveChangesAsync();

            return Ok(new { message = "Back office user created.", id = user.Id });
        }

        // PUT /api/admin/backoffice/users/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] SaveBoUserDto req)
        {
            var user = await _db.BackOfficeUsers.Include(u => u.Permissions).FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound(new { message = "User not found." });

            if (await _db.BackOfficeUsers.AnyAsync(u => u.Email == req.Email && u.Id != id))
                return Conflict(new { message = "Email already in use." });

            user.Name = req.Name;
            user.Email = req.Email;
            user.Mobile = req.Mobile;
            user.Role = req.Role ?? user.Role;
            user.RoleName = GetRoleDisplayName(req.Role ?? user.Role);
            if (!string.IsNullOrEmpty(req.Password) && req.Password.Length >= 8)
                user.Password = BCrypt.Net.BCrypt.HashPassword(req.Password);

            // Replace permissions
            _db.BoPermissions.RemoveRange(user.Permissions);
            foreach (var mod in req.Modules ?? new List<string>())
                _db.BoPermissions.Add(new BoPermission { UserId = user.Id, Module = mod });
            await _db.SaveChangesAsync();

            return Ok(new { message = "User updated." });
        }

        // PATCH /api/admin/backoffice/users/{id}/toggle
        [HttpPatch("{id}/toggle")]
        public async Task<IActionResult> Toggle(int id)
        {
            var user = await _db.BackOfficeUsers.FindAsync(id);
            if (user == null) return NotFound();
            user.IsActive = !user.IsActive;
            await _db.SaveChangesAsync();
            return Ok(new { message = user.IsActive ? "User enabled." : "User disabled.", isActive = user.IsActive });
        }

        // DELETE /api/admin/backoffice/users/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _db.BackOfficeUsers.FindAsync(id);
            if (user == null) return NotFound();
            _db.BackOfficeUsers.Remove(user);
            await _db.SaveChangesAsync();
            return Ok(new { message = "User deleted." });
        }

        private static string GetRoleDisplayName(string? role) => role switch
        {
            "operations" => "Operations Manager",
            "content_manager" => "Content Manager",
            "support" => "Support Executive",
            _ => "Custom"
        };
    }

    // ════════════════════════════════════════════════════════════════
    // EMAIL CONFIG  —  Admin: /api/admin/email-config
    // ════════════════════════════════════════════════════════════════
//    [ApiController]
//    [Route("api/admin/email-config")]
//    [Authorize(Policy = "AdminOnly")]
//    public class EmailConfigController : ControllerBase
//    {
//        private readonly AppDbContext _db;
//        private readonly ILogger<EmailConfigController> _log;

//        public EmailConfigController(AppDbContext db, ILogger<EmailConfigController> log)
//        { _db = db; _log = log; }

//        // GET /api/admin/email-config
//        [HttpGet]
//        public async Task<IActionResult> Get()
//        {
//            var cfg = await _db.EmailConfigs.FirstOrDefaultAsync() ?? new EmailConfig();
//            return Ok(new EmailConfigDto
//            {
//                Id = cfg.Id,
//                SmtpHost = cfg.SmtpHost,
//                SmtpPort = cfg.SmtpPort,
//                SmtpUser = cfg.SmtpUser,
//                SmtpPassword = cfg.SmtpPassword,
//                FromName = cfg.FromName,
//                FromEmail = cfg.FromEmail,
//                IsActive = cfg.IsActive
//            });
//        }

//        // POST /api/admin/email-config  (upsert)
//        [HttpPost]
//        public async Task<IActionResult> Save([FromBody] EmailConfigDto dto)
//        {
//            var cfg = await _db.EmailConfigs.FirstOrDefaultAsync();
//            if (cfg == null) { cfg = new EmailConfig(); _db.EmailConfigs.Add(cfg); }

//            cfg.SmtpHost = dto.SmtpHost?.Trim();
//            cfg.SmtpPort = dto.SmtpPort > 0 ? dto.SmtpPort : 587;
//            cfg.SmtpUser = dto.SmtpUser?.Trim();
//            cfg.SmtpPassword = dto.SmtpPassword;
//            cfg.FromName = dto.FromName?.Trim();
//            cfg.FromEmail = dto.FromEmail?.Trim();
//            cfg.IsActive = true;
//            cfg.UpdatedAt = DateTime.UtcNow;

//            await _db.SaveChangesAsync();
//            return Ok(new { message = "Email configuration saved." });
//        }

//        // POST /api/admin/email-config/test
//        [HttpPost("test")]
//        public async Task<IActionResult> Test([FromBody] TestEmailDto dto)
//        {
//            if (string.IsNullOrWhiteSpace(dto.Email))
//                return BadRequest(new { message = "Target email address is required." });

//            var cfg = await _db.EmailConfigs.FirstOrDefaultAsync();
//            if (cfg == null || string.IsNullOrWhiteSpace(cfg.SmtpHost))
//                return BadRequest(new { message = "No SMTP configuration saved. Please save settings first." });

//            try
//            {
//                using var client = new System.Net.Mail.SmtpClient(cfg.SmtpHost, cfg.SmtpPort)
//                {
//                    EnableSsl = true,
//                    DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network,
//                    UseDefaultCredentials = false,
//                    Credentials = new System.Net.NetworkCredential(cfg.SmtpUser, cfg.SmtpPassword),
//                    Timeout = 15000
//                };

//                var fromAddr = new System.Net.Mail.MailAddress(
//                    cfg.FromEmail ?? cfg.SmtpUser ?? "noreply@regnum.in",
//                    cfg.FromName ?? "Regnum Digital"
//                );
//                var toAddr = new System.Net.Mail.MailAddress(dto.Email.Trim());

//                using var msg = new System.Net.Mail.MailMessage(fromAddr, toAddr)
//                {
//                    Subject = "✅ Regnum Digital — SMTP Test Email",
//                    IsBodyHtml = true,
//                    Body = $@"
//<div style=""font-family:sans-serif;max-width:480px;margin:0 auto;padding:32px 24px;"">
//  <h2 style=""color:#6d4aff;margin-bottom:8px;"">SMTP Configuration Working ✅</h2>
//  <p style=""color:#444;line-height:1.6;"">
//    This test email confirms your SMTP settings are correctly configured in
//    <strong>Regnum Digital Admin Panel</strong>.
//  </p>
//  <table style=""width:100%;border-collapse:collapse;margin:20px 0;font-size:13px;"">
//    <tr><td style=""padding:6px 0;color:#888;"">SMTP Host</td><td style=""color:#222;font-weight:600;"">{cfg.SmtpHost}:{cfg.SmtpPort}</td></tr>
//    <tr><td style=""padding:6px 0;color:#888;"">From</td><td style=""color:#222;font-weight:600;"">{fromAddr.DisplayName} &lt;{fromAddr.Address}&gt;</td></tr>
//    <tr><td style=""padding:6px 0;color:#888;"">Sent to</td><td style=""color:#222;font-weight:600;"">{dto.Email}</td></tr>
//    <tr><td style=""padding:6px 0;color:#888;"">Time</td><td style=""color:#222;font-weight:600;"">{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</td></tr>
//  </table>
//  <p style=""color:#aaa;font-size:12px;"">Regnum Digital · Automated test message</p>
//</div>"
//                };

//                await client.SendMailAsync(msg);
//                _log.LogInformation("Test email sent to {Email} via {Host}", dto.Email, cfg.SmtpHost);
//                return Ok(new { message = $"Test email delivered to {dto.Email} successfully." });
//            }
//            catch (System.Net.Mail.SmtpException ex)
//            {
//                _log.LogWarning("SMTP test failed: {Msg}", ex.Message);
//                return StatusCode(500, new { message = $"SMTP error ({(int)ex.StatusCode}): {ex.Message}" });
//            }
//            catch (Exception ex)
//            {
//                _log.LogError(ex, "Email test unexpected error");
//                return StatusCode(500, new { message = ex.Message });
//            }
//        }
//    }

    // ════════════════════════════════════════════════════════════════
    // EMAIL TEMPLATES  —  Admin: /api/admin/email-templates
    // ════════════════════════════════════════════════════════════════
    //[ApiController]
    //[Route("api/admin/email-templates")]
    //[Authorize(Policy = "AdminOnly")]
    //public class EmailTemplatesController : ControllerBase
    //{
    //    private readonly AppDbContext _db;
    //    public EmailTemplatesController(AppDbContext db) { _db = db; }

    //    // GET /api/admin/email-templates
    //    [HttpGet]
    //    public async Task<IActionResult> GetAll()
    //    {
    //        var templates = await _db.EmailTemplates.ToListAsync();

    //        // Seed defaults if empty
    //        if (!templates.Any())
    //        {
    //            var defaults = DefaultTemplates();
    //            _db.EmailTemplates.AddRange(defaults);
    //            await _db.SaveChangesAsync();
    //            templates = defaults;
    //        }
    //        return Ok(templates.Select(t => new { t.Id, templateKey = t.TemplateKey, t.Subject, bodyHtml = t.BodyHtml }));
    //    }

    //    // PUT /api/admin/email-templates/{key}
    //    [HttpPut("{key}")]
    //    public async Task<IActionResult> Update(string key, [FromBody] SaveEmailTemplateDto dto)
    //    {
    //        var t = await _db.EmailTemplates.FirstOrDefaultAsync(x => x.TemplateKey == key);
    //        if (t == null) return NotFound(new { message = "Template not found." });
    //        t.Subject = dto.Subject;
    //        t.BodyHtml = dto.BodyHtml;
    //        t.UpdatedAt = DateTime.UtcNow;
    //        await _db.SaveChangesAsync();
    //        return Ok(new { message = "Template saved." });
    //    }

    //    private static List<EmailTemplate> DefaultTemplates() => new()
    //{
    //    new EmailTemplate { TemplateKey = "otp_login",
    //        Subject  = "Your Regnum Digital OTP: {{OTP_CODE}}",
    //        BodyHtml = "<p>Your OTP is <strong>{{OTP_CODE}}</strong>. Valid for 10 minutes.</p>" },
    //    new EmailTemplate { TemplateKey = "partner_welcome",
    //        Subject  = "Welcome to Regnum Digital, {{PARTNER_NAME}}!",
    //        BodyHtml = "<p>Hi {{PARTNER_NAME}}, welcome aboard! Login at {{APP_URL}} with {{PARTNER_EMAIL}}.</p>" },
    //    new EmailTemplate { TemplateKey = "new_content",
    //        Subject  = "New {{CONTENT_TYPE}} available: {{CONTENT_TITLE}}",
    //        BodyHtml = "<p>Hi {{PARTNER_NAME}}, new content <strong>{{CONTENT_TITLE}}</strong> in {{CONTENT_CATEGORY}} is live. Visit {{APP_URL}}.</p>" },
    //    new EmailTemplate { TemplateKey = "password_reset",
    //        Subject  = "Reset your Regnum Digital password",
    //        BodyHtml = "<p>Click <a href=\"{{RESET_URL}}\">here</a> to reset your password. Link expires in 1 hour.</p>" },
    //};
    //}

}
