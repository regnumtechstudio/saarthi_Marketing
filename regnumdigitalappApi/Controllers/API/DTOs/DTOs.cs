namespace RegnumDigital.API.DTOs;

// ── AUTH ────────────────────────────────────────────────────
public record LoginRequest(string Email, string Password);
public record OtpVerifyRequest(string Identifier, string OtpCode, string UserType);
public record AuthResponse(string Token, string Name, string Email, string UserType);

// ── PARTNER ─────────────────────────────────────────────────
public record CreatePartnerRequest(
    string FullName, string Email, string Mobile,
    string? ArnNumber, string? BusinessName,
    int? RoleId, int? PlanId, string? Password);

public record PartnerDto(
    int Id, string FullName, string Email, string Mobile,
    string? ArnNumber, string? BusinessName,
    string? RoleName, string? PlanName, bool IsActive, string ApprovalStatus, DateTime CreatedAt);


// ── ROLE / PERMISSION ────────────────────────────────────────
public record RoleDto(int Id, string Name, List<PermissionDto> Permissions);
public record PermissionDto(int Id, string FeatureArea, bool CanView, bool CanEdit, bool CanDelete, bool CanExport);
public record SaveRoleRequest(string Name, List<PermissionDto> Permissions);

// ── PLAN ─────────────────────────────────────────────────────
public record PlanDto(int Id, string Name, decimal MonthlyPrice, decimal YearlyPrice, int? RoleId, string? RoleName, bool IsActive);
public record SavePlanRequest(string Name, decimal MonthlyPrice, decimal YearlyPrice, int? RoleId,bool IsPopular,bool IsVisible,int SortOrder,string Features);

// ── PROMO ────────────────────────────────────────────────────
public record PromoDto(int Id, string Code, string DiscountType, decimal DiscountValue, int? MaxUses, int UsedCount, DateTime? ExpiresAt, bool IsActive);
public record SavePromoRequest(string Code, string DiscountType, decimal DiscountValue, int? MaxUses, DateTime? ExpiresAt,int? PerUserLimit,int IsVisibleOnCheckout,string? VisibleDescription);

// ── EVENT ────────────────────────────────────────────────────
public record EventDto(int Id, string Title, string EventType, DateTime EventDate, string? SpeakerName, string? LocationLink, string Status);
public record SaveEventRequest(string Title, string EventType, DateTime EventDate, string? SpeakerName, string? LocationLink);

// ── CATEGORY ─────────────────────────────────────────────────
public record CategoryDto(int Id, string Name, int? ParentId, string? ParentName, List<CategoryDto>? Children);
public record SaveCategoryRequest(string Name, int? ParentId);

// ── AMC ──────────────────────────────────────────────────────
public record AmcDto(int Id, string Name, string? InstagramUrl, string? YoutubeChannelId, bool AutoSyncEnabled);
public record SaveAmcRequest(string Name, string? InstagramUrl, string? YoutubeChannelId, bool AutoSyncEnabled);

// ── CONTENT ──────────────────────────────────────────────────
public record ContentAssetDto(
    int Id, string Title, string AssetType,
    int? CategoryId, string? CategoryName,
    int? AmcId, string? AmcName,
    string? FilePath, string? EmbedUrl,
    string Source, bool IsActive, DateTime CreatedAt);
public record SaveContentRequest(string Title, string AssetType, int? CategoryId, int? AmcId, string? EmbedUrl);

// ── DASHBOARD ────────────────────────────────────────────────
public record DashboardStats(int Downloads, int Collections, int UpcomingEvents);

// ── COBRAND ──────────────────────────────────────────────────
public record CobrandDto(string? BrandName, string? ArnNumber, string? Email, string? Mobile, string PrimaryColor, string FontColor, string FontStyle,string? LogoPath, string? PhotoPath);
//public record CobrandSaveRequest(string? BrandName, string? ArnNumber, string? Email, string? Mobile, string? PrimaryColor, string FontColor,string? FontStyle);
public record CobrandSaveRequest(string? brand_name, string? arn_number, string? Email, string? Mobile, string? primary_color, string font_color, string? font_style);

// ── LMS ──────────────────────────────────────────────────────
public record CourseDto(int Id, string Title, string? Description, List<LessonDto> Lessons);
public record LessonDto(int Id, string Title, string LessonType, int SortOrder, string? AssetUrl);
public record SaveCourseRequest(string Title, string? Description);
public record SaveLessonRequest(int CourseId, string Title, string LessonType, int SortOrder, int? AssetId);

// ── ACTIVITY ─────────────────────────────────────────────────
public record ActivityDto(int Id, string Action, string? AssetName, DateTime CreatedAt);

// ── v4 ADDITIONS ────────────────────────────────────────────

// Partner approval
public record PartnerApprovalRequest(string Action, string? RejectionReason); // action: approve|reject

// Forgot password
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

// Email config
public record EmailConfigDto(int Id, string SmtpHost, int SmtpPort, string SmtpUser, string SmtpPassword, string FromName, string FromEmail, bool IsActive);

// Email templates
public record EmailTemplateDto(int Id, string TemplateKey, string Subject, string BodyHtml, bool IsActive);
public record SaveEmailTemplateRequest(string Subject, string BodyHtml);

// --- Subscription ---
public record SubscriptionDto(
    bool IsActive,
    int? PlanId,
    string? PlanName,
    string? BillingCycle,
    DateTime? StartDate,
    DateTime? EndDate,
    decimal? AmountPaid,
    string? Status,
    string[]? Features
);

// --- Admin Subscriptions ---
public record ManualActivateRequest(
    int PartnerId,
    int PlanId,
    string BillingCycle,
    int? DurationDays,
    string? Reason
);
public record CreateOrderResponse(
      int OrderId,
      bool IsFree,
      decimal FinalAmount,
      // Populated only if IsFree=false:
      long? Amount,            // paise
      string? RazorpayOrderId,
      string? RazorpayKeyId,
      string? PlanName,
      string? PartnerName,
      string? PartnerEmail,
      string? PartnerMobile
  );
// --- Auth ---
public record OtpVerifyResponse(
    bool NeedsPlanSelection,
    string? TempToken,
    string? Token,
    int PartnerId,
    string Name,
    string Email,
    string? Mobile,
    string? ArnNumber,
    bool CobrandSetupDone,
    int? RoleId,
    string UserType = "partner"
);
public record CreateOrderRequest(
       int PlanId,
       string BillingCycle,
       string? PromoCode
   );
public record ActivateZeroRequest(int OrderId);

// --- Payment ---
public record VerifyPaymentRequest(
    int OrderId,
    string RazorpayPaymentId,
    string RazorpayOrderId,
    string RazorpaySignature
);
// --- Checkout ---
public record ApplyPromoRequest(
    int PlanId,
    string PromoCode,
    string BillingCycle
);
public record ApplyPromoResponse(
    bool Valid,
    decimal OriginalAmount,
    decimal DiscountAmount,
    decimal FinalAmount,
    bool IsFree,
    string Message
);
public record VisiblePromoDto(
    string Code,
    string Description,
    string DiscountType,
    decimal DiscountValue
);
