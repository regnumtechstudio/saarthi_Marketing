using System;
using System.Data.Entity;
//using System.Data.Entity;
using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegnumDigital.API.Data;
using RegnumDigital.API.Models;

namespace RegnumDigital.API.Services;

public class EmailService
{
    //private readonly IDbContextFactory<AppDbContext> _context;
    private readonly IServiceScopeFactory _scopeFactory;
    //private readonly AppDbContext _db;
    private readonly ILogger<EmailService> _log;

    public EmailService(IServiceScopeFactory scopeFactory,ILogger<EmailService> log)
    {
        _scopeFactory = scopeFactory; 
        _log = log;
    }


    // ── Send using DB config — fully safe, never throws ────────
    public async Task<bool> SendAsync(string toEmail, string toName, string subject, string bodyHtml)
    {
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                // var context = scope.ServiceProvider.GetRequiredService<DbContext>();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                // now do your work

                var cfg = dbContext.EmailConfigs.Where(c => c.IsActive).FirstOrDefault();
                if (cfg == null || string.IsNullOrWhiteSpace(cfg.SmtpUser) || string.IsNullOrWhiteSpace(cfg.FromEmail))
                {
                    _log.LogWarning("EmailService: No SMTP config. Email to {Email} logged only.", toEmail);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n[EMAIL NOT SENT - No SMTP Config]");
                    Console.WriteLine($"  To:      {toEmail}");
                    Console.WriteLine($"  Subject: {subject}");
                    Console.ResetColor();
                    return false;
                }

                using var smtp = new SmtpClient(cfg.SmtpHost, cfg.SmtpPort);
                smtp.Credentials = new NetworkCredential(cfg.SmtpUser, cfg.SmtpPassword);
                smtp.EnableSsl = true;
                smtp.Timeout = 10000;

                var msg = new MailMessage();
                msg.From = new MailAddress(cfg.FromEmail, cfg.FromName);
                msg.To.Add(new MailAddress(toEmail, toName));
                msg.Subject = subject;
                msg.Body = bodyHtml;
                msg.IsBodyHtml = true;

                await smtp.SendMailAsync(msg);
                _log.LogInformation("Email sent to {Email}", toEmail);
            }
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send email to {Email}", toEmail);
            return false;
        }
    }

    // ── Template helpers — each fully try/caught ───────────────
    public async Task SendOtpEmailAsync(string toEmail, string toName, string otpCode)
    {
        try
        {

            using (var scope = _scopeFactory.CreateScope())
            {
                // var context = scope.ServiceProvider.GetRequiredService<DbContext>();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                //var tmpl = dbContext.EmailTemplates.FirstOrDefaultAsync(t => t.TemplateKey == "otp_login" && t.IsActive);
                var tmpl = dbContext.EmailTemplates.Where(t => t.TemplateKey == "otp_login" && t.IsActive).FirstOrDefault();
                if (tmpl == null) return;
                var body = tmpl.BodyHtml.Replace("{{OTP_CODE}}", otpCode);
                await SendAsync(toEmail, toName, tmpl.Subject, body);
            }
        }
        catch (Exception ex) { _log.LogError(ex, "SendOtpEmail failed"); }
    }
    public async Task SendOtpEmailAsync_New(string toEmail, string toName, string otpCode)
    {
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {

                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tmpl = dbContext.EmailTemplates.Where(t => t.TemplateKey == "otp_login" && t.IsActive).FirstOrDefault(); //FirstOrDefaultAsync(t => t.TemplateKey == "otp_login" && t.IsActive);
                
                if (tmpl == null) return;
                var body = tmpl.BodyHtml.Replace("{{OTP_CODE}}", otpCode);
                await SendAsync(toEmail, toName, tmpl.Subject, body);
            }
        }
        catch (Exception ex) { _log.LogError(ex, "SendOtpEmail failed"); }
    }

    public async Task SendWelcomeEmailAsync(string toEmail, string partnerName, string appUrl)
    {
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                //var tmpl = await _db.EmailTemplates.FirstOrDefaultAsync(t => t.TemplateKey == "partner_welcome" && t.IsActive);
                var tmpl = _db.EmailTemplates.Where(t => t.TemplateKey == "partner_welcome" && t.IsActive).FirstOrDefault();
                if (tmpl == null) return;
                var body = tmpl.BodyHtml
                    .Replace("{{PARTNER_NAME}}", partnerName)
                    .Replace("{{PARTNER_EMAIL}}", toEmail)
                    .Replace("{{APP_URL}}", appUrl);
                await SendAsync(toEmail, partnerName, tmpl.Subject, body);
            }
        }
        catch (Exception ex) { _log.LogError(ex, "SendWelcomeEmail failed"); }
    }

    public async Task SendNewContentEmailAsync(string toEmail, string partnerName,
        string contentTitle, string contentType, string contentCategory, string appUrl)
    {
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {

                var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                //var tmpl = await _db.EmailTemplates.FirstOrDefaultAsync(t => t.TemplateKey == "new_content" && t.IsActive);
                var tmpl = _db.EmailTemplates.Where(t => t.TemplateKey == "new_content" && t.IsActive).FirstOrDefault();
                if (tmpl == null) return;
                var body = tmpl.BodyHtml
                    .Replace("{{PARTNER_NAME}}", partnerName)
                    .Replace("{{CONTENT_TITLE}}", contentTitle)
                    .Replace("{{CONTENT_TYPE}}", contentType)
                    .Replace("{{CONTENT_CATEGORY}}", contentCategory)
                    .Replace("{{APP_URL}}", appUrl);
                await SendAsync(toEmail, partnerName, tmpl.Subject, body);
            }
        }
        catch (Exception ex) { _log.LogError(ex, "SendNewContentEmail failed"); }
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetUrl)
    {
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                //var tmpl = await _db.EmailTemplates.FirstOrDefaultAsync(t => t.TemplateKey == "password_reset" && t.IsActive);
                var tmpl = _db.EmailTemplates.Where(t => t.TemplateKey == "password_reset" && t.IsActive).FirstOrDefault();
                if (tmpl == null) return;
                var body = tmpl.BodyHtml.Replace("{{RESET_URL}}", resetUrl);
                await SendAsync(toEmail, toName, tmpl.Subject, body);
            }
        }
        catch (Exception ex) { _log.LogError(ex, "SendPasswordResetEmail failed"); }
    }

    public static List<EmailTemplate> DefaultTemplates() => new()
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
