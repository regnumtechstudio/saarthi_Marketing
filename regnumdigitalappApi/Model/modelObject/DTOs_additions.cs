using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace modelObject
{
    public class DTOs_additions
    {
        // ============================================================
        // APPEND these records/classes to the bottom of DTOs.cs
        // ============================================================

        // FCM Config
        public record FcmConfigDto
        {
            public string? ProjectId { get; init; }
            public string? ServerKey { get; init; }
            public string? VapidKey { get; init; }
            public string? ApiKey { get; init; }
            public string? AuthDomain { get; init; }
            public string? AppId { get; init; }
        }

        // Push notification send request
        public class SendPushDto
        {
            public string Title { get; set; } = "";
            public string Body { get; set; } = "";
            public string? ClickUrl { get; set; }
            public string? IconUrl { get; set; }
            public string? Audience { get; set; } = "all";   // all | specific | role
            public int? PartnerId { get; set; }
            public int? RoleId { get; set; }
        }

        // Test push DTO
        public class SendTestPushDto
        {
            public string? AdminEmail { get; set; }
        }

        // FCM token registration from partner PWA
        public class RegisterFcmTokenDto
        {
            public string Token { get; set; } = "";
            public string? Platform { get; set; } = "web";
        }

        // Back office user save/update
        public class SaveBoUserDto
        {
            public string Name { get; set; } = "";
            public string Email { get; set; } = "";
            public string? Mobile { get; set; }
            public string? Password { get; set; }
            public string? Role { get; set; }
            public List<string>? Modules { get; set; }
        }

        // Email Config DTO
        public class EmailConfigDto
        {
            public string? SmtpHost { get; set; }
            public int SmtpPort { get; set; } = 587;
            public string? SmtpUser { get; set; }
            public string? SmtpPassword { get; set; }
            public string? FromName { get; set; }
            public string? FromEmail { get; set; }
            public bool IsActive { get; set; } = true;
            public int Id { get; set; }
        }

        // Email config test request
        public class TestEmailDto
        {
            public string Email { get; set; } = "";
        }

        // Email template save DTO
        public class SaveEmailTemplateDto
        {
            public string Subject { get; set; } = "";
            public string BodyHtml { get; set; } = "";
        }

        

    }
}
