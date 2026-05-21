using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RegnumDigital.API.Models;

[Table("admin_users")]
public class AdminUser
{
    [Key] public int Id { get; set; }
    [Column("email")] public string Email { get; set; } = "";
    [Column("password")] public string Password { get; set; } = "";
    [Column("name")] public string Name { get; set; } = "";
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("roles")]
public class Role
{
    [Key] public int Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
}

[Table("permissions")]
public class Permission
{
    [Key] public int Id { get; set; }
    [Column("role_id")] public int RoleId { get; set; }
    [Column("feature_area")] public string FeatureArea { get; set; } = "";
    [Column("can_view")] public bool CanView { get; set; }
    [Column("can_edit")] public bool CanEdit { get; set; }
    [Column("can_delete")] public bool CanDelete { get; set; }
    [Column("can_export")] public bool CanExport { get; set; }
    public Role? Role { get; set; }
}

[Table("plans")]
public class Plan
{
    [Key] public int Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("monthly_price")] public decimal MonthlyPrice { get; set; }
    [Column("yearly_price")] public decimal YearlyPrice { get; set; }
    [Column("role_id")] public int? RoleId { get; set; }
    [Column("is_active")] public bool IsActive { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public bool IsPopular { get; set; } = true;
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Role? Role { get; set; }
    public int? SortOrder { get; set; }
    public string FeaturesJson { get; set; }

}

[Table("partners")]
public class Partner
{
    [Key] public int Id { get; set; }
    [Column("full_name")] public string FullName { get; set; } = "";
    [Column("email")] public string Email { get; set; } = "";
    [Column("mobile")] public string Mobile { get; set; } = "";
    [Column("arn_number")] public string? ArnNumber { get; set; }
    [Column("business_name")] public string? BusinessName { get; set; }
    [Column("role_id")] public int? RoleId { get; set; }
    [Column("plan_id")] public int? PlanId { get; set; }
    [Column("password")] public string? Password { get; set; }
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("approval_status")] public string ApprovalStatus { get; set; } = "approved";
    [Column("rejection_reason")] public string? RejectionReason { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Role? Role { get; set; }
    public Plan? Plan { get; set; }
    public string? Status { get; set; }
  public bool IsFirstLogin { get; set; } = true;
    public  DateTime? LastLoginAt { get; set; }
  public bool CobrandSetupDone { get; set; } = true;
}

[Table("otp_store")]
public class OtpStore
{
    [Key] public int Id { get; set; }
    [Column("identifier")] public string Identifier { get; set; } = "";
    [Column("otp_code")] public string OtpCode { get; set; } = "";
    [Column("user_type")] public string UserType { get; set; } = "";
    [Column("expires_at")] public DateTime ExpiresAt { get; set; }
    [Column("is_used")] public bool IsUsed { get; set; } = false;
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("promo_codes")]
public class PromoCode
{
    [Key] public int Id { get; set; }
    [Column("code")] public string Code { get; set; } = "";
    [Column("discount_type")] public string DiscountType { get; set; } = "";
    [Column("discount_value")] public decimal DiscountValue { get; set; }
    [Column("max_uses")] public int? MaxUses { get; set; }
    [Column("used_count")] public int UsedCount { get; set; } = 0;
    [Column("expires_at")] public DateTime? ExpiresAt { get; set; }
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CurrentUses { get; set; }
    public int PerUserLimit { get; set; }
    public bool IsVisibleOnCheckout { get; set; } = true;
    public string ApplicablePlansJson { get; set; }
    public string VisibleDescription { get; set; } 
}

public class CheckoutOrder
{
    public int Id { get; set; }
    public Guid IdempotencyKey { get; set; } = Guid.NewGuid();
    public int PartnerId { get; set; }
    public int PlanId { get; set; }
    public string BillingCycle { get; set; } = "monthly";
    public int? PromoCodeId { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public string Status { get; set; } = "Pending";
    public string? GatewayOrderId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? AbandonedAt { get; set; }

    public Partner? Partner { get; set; }
    public Plan? Plan { get; set; }
    public PromoCode? PromoCode { get; set; }
    public Payment? Payment { get; set; }
}

public class Payment
{
    public int Id { get; set; }
    public int CheckoutOrderId { get; set; }
    public int PartnerId { get; set; }
    public string GatewayPaymentId { get; set; } = "";
    public string GatewayOrderId { get; set; } = "";
    public string? GatewaySignature { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public string Status { get; set; } = "Initiated";
    public bool IsWebhookVerified { get; set; }
    public string? RawWebhookPayload { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CapturedAt { get; set; }

    public CheckoutOrder? CheckoutOrder { get; set; }
    public Partner? Partner { get; set; }
}

public class PartnerSubscription
{
    public int Id { get; set; }
    public int PartnerId { get; set; }
    public int PlanId { get; set; }
    public int CheckoutOrderId { get; set; }
    public string BillingCycle { get; set; } = "monthly";
    public decimal AmountPaid { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsComplimentary { get; set; }
    public string? Notes { get; set; }
    public string? ActivatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAt { get; set; }

    public Plan? Plan { get; set; }
    public Partner? Partner { get; set; }
}



public class PlanEntitlement
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int? CategoryId { get; set; }
    public int? ContentItemId { get; set; }
    public bool AllContent { get; set; }
    public bool IncludeTeaser { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Plan? Plan { get; set; }
}

public class PromoCodeUsage
{
    public int Id { get; set; }
    public int PromoCodeId { get; set; }
    public int PartnerId { get; set; }
    public int CheckoutOrderId { get; set; }
    public DateTime UsedAt { get; set; } = DateTime.UtcNow;

    public PromoCode? PromoCode { get; set; }
    public Partner? Partner { get; set; }
}

[Table("events")]
public class Event
{
    [Key] public int Id { get; set; }
    [Column("title")] public string Title { get; set; } = "";
    [Column("event_type")] public string EventType { get; set; } = "webinar";
    [Column("event_date")] public DateTime EventDate { get; set; }
    [Column("speaker_name")] public string? SpeakerName { get; set; }
    [Column("location_link")] public string? LocationLink { get; set; }
    [Column("status")] public string Status { get; set; } = "upcoming";
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("categories")]
public class Category
{
    [Key] public int Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("parent_id")] public int? ParentId { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();
}

[Table("amc_master")]
public class AmcMaster
{
    [Key] public int Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("instagram_url")] public string? InstagramUrl { get; set; }
    [Column("youtube_channel_id")] public string? YoutubeChannelId { get; set; }
    [Column("auto_sync_enabled")] public bool AutoSyncEnabled { get; set; } = false;
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CobrandProfile
{
    public int Id { get; set; }
    public int PartnerId { get; set; }
    public string? BrandName { get; set; }
    public string? ArnNumber { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public string? PhotoUrl { get; set; }
    public string PrimaryColor { get; set; } = "#1350A3";
    public string FontColor { get; set; } = "#FFFFFF";
    public string FontStyle { get; set; } = "Sora";

    public Partner? Partner { get; set; }
}


public class ContentItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string ContentType { get; set; } = "image";
    public string? FileUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int? CategoryId { get; set; }
    public int? AmcId { get; set; }
    public string? Tags { get; set; }
    public bool RequiresPlanAccess { get; set; }
    public bool IsActive { get; set; } = true;
    public int DownloadCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuditLog
{
    public long Id { get; set; }
    public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
    public string Action { get; set; } = "";
    public string ActorType { get; set; } = "system";
    public int? ActorId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}


[Table("content_assets")]
public class ContentAsset
{
    [Key] public int Id { get; set; }
    [Column("title")] public string Title { get; set; } = "";
    [Column("asset_type")] public string AssetType { get; set; } = "";
    [Column("category_id")] public int? CategoryId { get; set; }
    [Column("amc_id")] public int? AmcId { get; set; }
    [Column("file_path")] public string? FilePath { get; set; }
    [Column("embed_url")] public string? EmbedUrl { get; set; }
    [Column("source")] public string Source { get; set; } = "manual";
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Category? Category { get; set; }
    public AmcMaster? Amc { get; set; }
}

[Table("partner_collections")]
public class PartnerCollection
{
    [Key] public int Id { get; set; }
    [Column("partner_id")] public int PartnerId { get; set; }
    [Column("asset_id")] public int AssetId { get; set; }
    [Column("saved_at")] public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    public Partner? Partner { get; set; }
    public ContentAsset? Asset { get; set; }
}

[Table("partner_activity")]
public class PartnerActivity
{
    [Key] public int Id { get; set; }
    [Column("partner_id")] public int PartnerId { get; set; }
    [Column("action")] public string Action { get; set; } = "";
    [Column("asset_id")] public int? AssetId { get; set; }
    [Column("asset_name")] public string? AssetName { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Partner? Partner { get; set; }
}

[Table("partner_cobrand")]
public class PartnerCobrand
{
    [Key] public int Id { get; set; }
    [Column("partner_id")] public int PartnerId { get; set; }
    [Column("brand_name")] public string? BrandName { get; set; }
    [Column("arn_number")] public string? ArnNumber { get; set; }
    [Column("email")] public string? Email { get; set; }
    [Column("mobile")] public string? Mobile { get; set; }
    [Column("primary_color")] public string PrimaryColor { get; set; } = "#00B386";
    [Column("font_color")] public string FontColor { get; set; } = "#FFFFFF";
    [Column("font_style")] public string FontStyle { get; set; } = "Sora";
    [Column("logo_path")] public string? LogoPath { get; set; }
    [Column("photo_path")] public string? PhotoPath { get; set; }
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Partner? Partner { get; set; }
}

[Table("event_registrations")]
public class EventRegistration
{
    [Key] public int Id { get; set; }
    [Column("event_id")] public int EventId { get; set; }
    [Column("partner_id")] public int PartnerId { get; set; }
    [Column("registered_at")] public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public Event? Event { get; set; }
    public Partner? Partner { get; set; }
}

[Table("lms_courses")]
public class LmsCourse
{
    [Key] public int Id { get; set; }
    [Column("title")] public string Title { get; set; } = "";
    [Column("description")] public string? Description { get; set; }
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<LmsLesson> Lessons { get; set; } = new List<LmsLesson>();
}

[Table("lms_lessons")]
public class LmsLesson
{
    [Key] public int Id { get; set; }
    [Column("course_id")] public int CourseId { get; set; }
    [Column("title")] public string Title { get; set; } = "";
    [Column("lesson_type")] public string LessonType { get; set; } = "video";
    [Column("sort_order")] public int SortOrder { get; set; } = 0;
    [Column("asset_id")] public int? AssetId { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public LmsCourse? Course { get; set; }
    public ContentAsset? Asset { get; set; }
}

// ── v4 ADDITIONS ──────────────────────────────────────────────

[Table("password_reset_tokens")]
public class PasswordResetToken
{
    [Key] public int Id { get; set; }
    [Column("identifier")] public string Identifier { get; set; } = "";
    [Column("user_type")] public string UserType { get; set; } = "";
    [Column("token")] public string Token { get; set; } = "";
    [Column("expires_at")] public DateTime ExpiresAt { get; set; }
    [Column("is_used")] public bool IsUsed { get; set; } = false;
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("email_config")]
public class EmailConfig
{
    [Key] public int Id { get; set; }
    [Column("smtp_host")] public string SmtpHost { get; set; } = "smtp.gmail.com";
    [Column("smtp_port")] public int SmtpPort { get; set; } = 587;
    [Column("smtp_user")] public string SmtpUser { get; set; } = "";
    [Column("smtp_password")] public string SmtpPassword { get; set; } = "";
    [Column("from_name")] public string FromName { get; set; } = "Regnum Digital";
    [Column("from_email")] public string FromEmail { get; set; } = "";
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("email_templates")]
public class EmailTemplate
{
    [Key] public int Id { get; set; }
    [Column("template_key")] public string TemplateKey { get; set; } = "";
    [Column("subject")] public string Subject { get; set; } = "";
    [Column("body_html")] public string BodyHtml { get; set; } = "";
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}


[Table("fcm_config")]
public class FcmConfig
{
    [Key] public int Id { get; set; }
    [Column("project_id")] public string? ProjectId { get; set; }
    [Column("server_key")] public string? ServerKey { get; set; }
    [Column("vapid_key")] public string? VapidKey { get; set; }
    [Column("api_key")] public string? ApiKey { get; set; }
    [Column("auth_domain")] public string? AuthDomain { get; set; }
    [Column("app_id")] public string? AppId { get; set; }
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("fcm_tokens")]
public class FcmToken
{
    [Key] public int Id { get; set; }
    [Column("partner_id")] public int PartnerId { get; set; }
    [Column("token")] public string Token { get; set; } = "";
    [Column("platform")] public string Platform { get; set; } = "web";
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Partner Partner { get; set; } = null!;
}

[Table("push_notifications")]
public class PushNotification
{
    [Key] public int Id { get; set; }
    [Column("title")] public string Title { get; set; } = "";
    [Column("body")] public string Body { get; set; } = "";
    [Column("click_url")] public string? ClickUrl { get; set; }
    [Column("icon_url")] public string? IconUrl { get; set; }
    [Column("audience")] public string Audience { get; set; } = "all";
    [Column("partner_id")] public int? PartnerId { get; set; }
    [Column("role_id")] public int? RoleId { get; set; }
    [Column("sent_count")] public int SentCount { get; set; } = 0;
    [Column("opened_count")] public int OpenedCount { get; set; } = 0;
    [Column("sent_by")] public int? SentBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("back_office_users")]
public class BackOfficeUser
{
    [Key] public int Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("email")] public string Email { get; set; } = "";
    [Column("mobile")] public string? Mobile { get; set; }
    [Column("password")] public string Password { get; set; } = "";
    [Column("role")] public string Role { get; set; } = "custom";
    [Column("role_name")] public string? RoleName { get; set; }
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("created_by")] public int? CreatedBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<BoPermission> Permissions { get; set; } = new List<BoPermission>();
}

[Table("bo_permissions")]
public class BoPermission
{
    [Key] public int Id { get; set; }
    [Column("user_id")] public int UserId { get; set; }
    [Column("module")] public string Module { get; set; } = "";
    public BackOfficeUser User { get; set; } = null!;
}

public class Collection { public int Id { get; set; } public int PartnerId { get; set; } public string Name { get; set; } = ""; }
public class CollectionItem { public int Id { get; set; } public int CollectionId { get; set; } public int ContentId { get; set; } }
//public class Event 
//{ public int Id { get; set; } 
//    public string Title { get; set; } = ""
//    public DateTime EventDate { get; set; } 
//}
//public class EventRegistration { public int Id { get; set; } public int EventId { get; set; } public int PartnerId { get; set; } }
public class ActivityLog
{
    public long Id { get; set; }
    public int PartnerId { get; set; }
    public string Action { get; set; }
}
public class ContentPlanMapping 
{ 
    public int Id { get; set; } 
    public int ContentId { get; set; } 
    public int PlanId { get; set; }
}
public enum PartnerStatus { PendingOtp, PendingPlanSelection, Active, Suspended }
public enum OrderStatus { Pending, AwaitingPayment, Paid, Activated, Failed, Abandoned, Cancelled }
public enum PaymentStatus { Initiated, Captured, Failed, Refunded }
public enum SubscriptionStatus { Active, Expired, Cancelled, Suspended }
public enum BillingCycle { Monthly, Yearly }
public enum DiscountType { Percentage, Flat }






