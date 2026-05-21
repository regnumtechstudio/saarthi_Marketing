using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RegnumDigital.API.Data;
using RegnumDigital.API.DTOs;
using RegnumDigital.API.Models;
using static RegnumDigital.API.Services.JwtService;


namespace RegnumDigital.API.Services;

public class JwtService
{
    private readonly IConfiguration _cfg;
    public JwtService(IConfiguration cfg) => _cfg = cfg;

    public string GenerateToken(int userId, string email, string name, string userType)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim("name", name),
            new Claim("userType", userType),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var token = new JwtSecurityToken(
            issuer:            _cfg["Jwt:Issuer"],
            audience:          _cfg["Jwt:Audience"],
            claims:            claims,
            expires:           DateTime.UtcNow.AddHours(double.Parse(_cfg["Jwt:ExpiryHours"]!)),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    // ─────────────────────────────────────────────
    // 5. JWT SERVICE
    // ─────────────────────────────────────────────

    public interface IJwtService
    {
        string GenerateTempToken(int partnerId, string email);
        string GenerateFullToken(int partnerId, string name, string email,
                                 int? roleId, int? planId, bool cobrandSetupDone);
        ClaimsPrincipal? ValidateToken(string token);
    }
        public string GenerateTempToken(int partnerId, string email)
        {
            var claims = new[] {
                new Claim("sub",   partnerId.ToString()),
                new Claim("email", email),
                new Claim("scope", "checkout")     // limited scope
            };
            return BuildToken(claims, minutes: 60);
        }

        public string GenerateFullToken(int partnerId, string name, string email,
                                        int? roleId, int? planId, bool cobrandSetupDone)
        {
            var claims = new[] {
                new Claim("sub",             partnerId.ToString()),
                new Claim("name",            name),
                new Claim("email",           email),
                new Claim("roleId",          (roleId ?? 0).ToString()),
                new Claim("planId",          (planId ?? 0).ToString()),
                new Claim("cobrandSetupDone",cobrandSetupDone.ToString().ToLower()),
                new Claim("scope",           "full"),
                new Claim("userType",        "partner")
            };
            return BuildToken(claims, days: 30);
        }

        private string BuildToken(Claim[] claims, int? minutes = null, int? days = null)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Secret"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = minutes.HasValue
                ? DateTime.UtcNow.AddMinutes(minutes.Value)
                : DateTime.UtcNow.AddDays(days!.Value);
            var token = new JwtSecurityToken(
                issuer: _cfg["Jwt:Issuer"],
                claims: claims,
                expires: expires,
                signingCredentials: creds
            );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public ClaimsPrincipal? ValidateToken(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Secret"]!));
                return handler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out _);
            }
            catch { return null; }
        }
    }


// ─────────────────────────────────────────────
// 6. PROMO ENGINE
// ─────────────────────────────────────────────

    public interface IPromoEngine
    {
        PromoCalculationResult Calculate(decimal originalAmount, dynamic promo);
        void ValidatePromo(dynamic promo, int partnerId, int planId,
                           int partnerUsageCount, bool isPlanApplicable);
    }

    public record PromoCalculationResult(
        decimal DiscountAmount,
        decimal FinalAmount,
        bool IsFree,
        string Message
    );

    public class PromoEngine : IPromoEngine
    {
        public PromoCalculationResult Calculate(decimal originalAmount, dynamic promo)
        {
            decimal discount = promo.DiscountType == "percentage"
                ? Math.Round(originalAmount * (decimal)promo.DiscountValue / 100m, 2)
                : (decimal)promo.DiscountValue;

            // Cap: discount cannot exceed original amount
            discount = Math.Min(discount, originalAmount);

            // Final amount cannot go negative
            var final = Math.Max(0m, originalAmount - discount);

            var msg = promo.DiscountType == "percentage"
                ? $"{promo.DiscountValue}% discount applied! You save ₹{discount:F0}"
                : $"₹{discount:F0} discount applied!";

            return new PromoCalculationResult(discount, final, final == 0m, msg);
        }

        public void ValidatePromo(dynamic promo, int partnerId, int planId,
                                  int partnerUsageCount, bool isPlanApplicable)
        {
            if (!promo.IsActive)
                throw new BusinessException("Promo code is inactive.");

            if (promo.ExpiresAt != null && (DateTime)promo.ExpiresAt < DateTime.UtcNow)
                throw new BusinessException("Promo code has expired.");

            if (promo.MaxUses != null && (int)promo.CurrentUses >= (int)promo.MaxUses)
                throw new BusinessException("Promo code usage limit reached.");

            if (!isPlanApplicable)
                throw new BusinessException("This promo code is not applicable for the selected plan.");

            if (promo.PerUserLimit != null && partnerUsageCount >= (int)promo.PerUserLimit)
                throw new BusinessException("You have already used this promo code the maximum number of times.");
        }
    }

    public class BusinessException : Exception
    {
        public BusinessException(string message) : base(message) { }
    }


// ─────────────────────────────────────────────
// 7. CHECKOUT SERVICE
// ─────────────────────────────────────────────

    public interface ICheckoutService
    {
        Task<ApplyPromoResponse> ApplyPromoAsync(int partnerId, ApplyPromoRequest req);
        Task<CreateOrderResponse> CreateOrderAsync(int partnerId, CreateOrderRequest req);
        Task<OtpVerifyResponse> ActivateZeroOrderAsync(int partnerId, int orderId);
    }

    public class CheckoutService : ICheckoutService
    {
        private readonly AppDbContext _db;
        private readonly IPromoEngine _promo;
        private readonly IPaymentService _payment;
        private readonly ISubscriptionService _subscription;
        private readonly IJwtService _jwt;
        private readonly IAuditService _audit;

        public CheckoutService(AppDbContext db, IPromoEngine promo,
            IPaymentService payment, ISubscriptionService subscription,
            IJwtService jwt, IAuditService audit)
        {
            _db = db; _promo = promo; _payment = payment;
            _subscription = subscription; _jwt = jwt; _audit = audit;
        }

        public async Task<ApplyPromoResponse> ApplyPromoAsync(int partnerId, ApplyPromoRequest req)
        {
            var plan = await _db.Plans.FindAsync(req.PlanId)
                ?? throw new BusinessException("Plan not found.");
            var price = req.BillingCycle == "yearly" ? plan.YearlyPrice : plan.MonthlyPrice;

            var promoCode = await _db.PromoCodes
                .FirstOrDefaultAsync(p => p.Code == req.PromoCode.ToUpper())
                ?? throw new BusinessException("Invalid promo code.");

            // Check plan applicability
            bool isPlanApplicable = true;
           //// if (!string.IsNullOrEmpty(promoCode.ApplicablePlansJson))
           // {
           //     var planIds = JsonSerializer.Deserialize<int[]>(promoCode.ApplicablePlansJson) ?? [];
           //     isPlanApplicable = planIds.Length == 0 || planIds.Contains(req.PlanId);
           // }

            // Count partner's usage of this promo
            var usageCount = await _db.PromoCodeUsages
                .CountAsync(u => u.PromoCodeId == promoCode.Id && u.PartnerId == partnerId);

            _promo.ValidatePromo(promoCode, partnerId, req.PlanId, usageCount, isPlanApplicable);
            var result = _promo.Calculate(price, promoCode);

            return new ApplyPromoResponse(true, price, result.DiscountAmount,
                result.FinalAmount, result.IsFree, result.Message);
        }

        public async Task<CreateOrderResponse> CreateOrderAsync(int partnerId, CreateOrderRequest req)
        {
            // Check for existing active subscription
            var hasActive = await _db.PartnerSubscriptions
                .AnyAsync(s => s.PartnerId == partnerId && s.Status == "Active");
            if (hasActive) throw new BusinessException("You already have an active subscription.");

            var plan = await _db.Plans.FindAsync(req.PlanId)
                ?? throw new BusinessException("Plan not found.");
            var price = req.BillingCycle == "yearly" ? plan.YearlyPrice : plan.MonthlyPrice;

            decimal discountAmt = 0m;
            decimal finalAmt = price;
            int? promoId = null;

            // Re-validate promo at order creation time (prevent race conditions)
            if (!string.IsNullOrEmpty(req.PromoCode))
            {
                var applyResult = await ApplyPromoAsync(partnerId,
                    new ApplyPromoRequest(req.PlanId, req.PromoCode, req.BillingCycle));
                discountAmt = applyResult.DiscountAmount;
                finalAmt = applyResult.FinalAmount;
                var promoEnt = await _db.PromoCodes.FirstAsync(p => p.Code == req.PromoCode.ToUpper());
                promoId = promoEnt.Id;
            }

            var partner = await _db.Partners.FindAsync(partnerId)!;

            // Create order record
            var order = new CheckoutOrder
            {
                PartnerId = partnerId,
                PlanId = req.PlanId,
                BillingCycle = req.BillingCycle,
                PromoCodeId = promoId,
                OriginalAmount = price,
                DiscountAmount = discountAmt,
                FinalAmount = finalAmt,
                Status = "Pending"
            };

            if (finalAmt > 0)
            {
                // Create Razorpay order
                var rzpOrder = await _payment.CreateRazorpayOrderAsync(
                    (long)(finalAmt * 100), "INR", $"saarthi_order_{Guid.NewGuid():N}");
                order.GatewayOrderId = rzpOrder.Id;
                order.Status = "AwaitingPayment";
            }

            _db.CheckoutOrders.Add(order);
            await _db.SaveChangesAsync();

            await _audit.LogAsync("CheckoutOrder", order.Id, "OrderCreated", "partner", partnerId);

            if (finalAmt == 0)
                return new CreateOrderResponse(order.Id, true, finalAmt,
                    null, null, null, null, null, null, null);

            //var featuresArr = plan.FeaturesJson != null
            //    ? JsonSerializer.Deserialize<string[]>(plan.FeaturesJson) ?? []
            //    : Array.Empty<string>();

            return new CreateOrderResponse(
                order.Id, false, finalAmt,
                Amount: (long)(finalAmt * 100),
                RazorpayOrderId: order.GatewayOrderId,
                RazorpayKeyId: _payment.GetPublicKeyId(),
                PlanName: plan.Name,
               // PartnerName: partner!.Name,
                PartnerName: partner!.FullName,
                PartnerEmail: partner.Email,
                PartnerMobile: partner.Mobile
            );
        }

        public async Task<OtpVerifyResponse> ActivateZeroOrderAsync(int partnerId, int orderId)
        {
            var order = await _db.CheckoutOrders
                .Include(o => o.Plan)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.PartnerId == partnerId)
                ?? throw new BusinessException("Order not found.");

            if (order.FinalAmount != 0)
                throw new BusinessException("This order requires payment.");
            if (order.Status == "Activated")
                throw new BusinessException("Order already activated.");

            // Record promo usage
            if (order.PromoCodeId.HasValue)
            {
                _db.PromoCodeUsages.Add(new PromoCodeUsage
                {
                    PromoCodeId = order.PromoCodeId.Value,
                    PartnerId = partnerId,
                    CheckoutOrderId = order.Id
                });
                // Increment CurrentUses
                var promo = await _db.PromoCodes.FindAsync(order.PromoCodeId.Value);
                if (promo != null) promo.CurrentUses++;
            }

            // Activate subscription
            var sub = await _subscription.ActivateAsync(partnerId, order, "system");

            // Mark order activated
            order.Status = "Activated";
            order.CompletedAt = DateTime.UtcNow;

            // Update partner status
            var partner = await _db.Partners.FindAsync(partnerId)!;
            partner!.Status = "Active";
            partner.IsFirstLogin = true; // wizard will fire
            await _db.SaveChangesAsync();

            await _audit.LogAsync("CheckoutOrder", order.Id, "ZeroActivation", "system", partnerId);

            var token = _jwt.GenerateFullToken(
                partnerId, //partner.Name
                          partner.FullName, partner.Email,
                partner.RoleId, sub.PlanId, (bool)partner.CobrandSetupDone);

            return new OtpVerifyResponse(
                NeedsPlanSelection: false,
                TempToken: null,
                Token: token,
                PartnerId: partnerId,
               // Name: partner.Name,
                Name: partner.FullName,
                Email: partner.Email,
                Mobile: partner.Mobile,
                ArnNumber: partner.ArnNumber,
                CobrandSetupDone: (bool)partner.CobrandSetupDone,
                RoleId: partner.RoleId
            );
        }
    }

// ─────────────────────────────────────────────
// 8. PAYMENT SERVICE (Razorpay)
// ─────────────────────────────────────────────

    public interface IPaymentService
    {
        Task<RazorpayOrderResult> CreateRazorpayOrderAsync(long amountPaise, string currency, string receipt);
        bool VerifyPaymentSignature(string razorpayOrderId, string razorpayPaymentId, string signature);
        bool VerifyWebhookSignature(string rawBody, string signature);
        string GetPublicKeyId();
    }

    public record RazorpayOrderResult(string Id, long Amount, string Currency);

    public class RazorpayPaymentService : IPaymentService
    {
        private readonly string _keyId;
        private readonly string _keySecret;
        private readonly string _webhookSecret;
        private readonly HttpClient _http;

        public RazorpayPaymentService(IConfiguration cfg, HttpClient http)
        {
            _keyId = cfg["Razorpay:KeyId"]!;
            _keySecret = cfg["Razorpay:KeySecret"]!;
            _webhookSecret = cfg["Razorpay:WebhookSecret"]!;
            _http = http;
        }

        public string GetPublicKeyId() => _keyId;

        public async Task<RazorpayOrderResult> CreateRazorpayOrderAsync(
            long amountPaise, string currency, string receipt)
        {
            var auth = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_keyId}:{_keySecret}"));
            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", $"Basic {auth}");

            var payload = JsonSerializer.Serialize(new
            {
                amount = amountPaise,
                currency,
                receipt
            });
            var response = await _http.PostAsync(
                "https://api.razorpay.com/v1/orders",
                new StringContent(payload, Encoding.UTF8, "application/json"));

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json).RootElement;
            return new RazorpayOrderResult(
                doc.GetProperty("id").GetString()!,
                doc.GetProperty("amount").GetInt64(),
                doc.GetProperty("currency").GetString()!
            );
        }

        // ── Frontend payment signature: HMAC-SHA256(orderId + "|" + paymentId) ──
        public bool VerifyPaymentSignature(
            string razorpayOrderId,
            string razorpayPaymentId,
            string signature)
        {
            var data = $"{razorpayOrderId}|{razorpayPaymentId}";
            var keyBytes = Encoding.UTF8.GetBytes(_keySecret);
            var msgBytes = Encoding.UTF8.GetBytes(data);
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(msgBytes);
            var expected = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            // Constant-time comparison to prevent timing attacks
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));
        }

        // ── Webhook signature: HMAC-SHA256(rawBody) using webhookSecret ──
        public bool VerifyWebhookSignature(string rawBody, string signature)
        {
            var keyBytes = Encoding.UTF8.GetBytes(_webhookSecret);
            var msgBytes = Encoding.UTF8.GetBytes(rawBody);
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(msgBytes);
            var expected = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));
        }
    }

// ─────────────────────────────────────────────
// 9. SUBSCRIPTION SERVICE
// ─────────────────────────────────────────────
    public interface ISubscriptionService
    {
        Task<PartnerSubscription> ActivateAsync(
            int partnerId, CheckoutOrder order, string activatedBy);
        Task<SubscriptionDto> GetActiveSubscriptionAsync(int partnerId);
    }

    public class SubscriptionService : ISubscriptionService
    {
        private readonly AppDbContext _db;
        private readonly IAuditService _audit;

        public SubscriptionService(AppDbContext db, IAuditService audit)
        { _db = db; _audit = audit; }

        public async Task<PartnerSubscription> ActivateAsync(
            int partnerId, CheckoutOrder order, string activatedBy)
        {
            // Cancel any existing active subscription (should not happen due to DB constraint,
            // but guard defensively)
            var existing = await _db.PartnerSubscriptions
                .Where(s => s.PartnerId == partnerId && s.Status == "Active")
                .FirstOrDefaultAsync();
            if (existing != null)
            {
                existing.Status = "Cancelled";
                existing.CancelledAt = DateTime.UtcNow;
            }

            var now = DateTime.UtcNow;
            var endDate = order.BillingCycle == "yearly"
                ? now.AddDays(365) : now.AddDays(30);

            var sub = new PartnerSubscription
            {
                PartnerId = partnerId,
                PlanId = order.PlanId,
                CheckoutOrderId = order.Id,
                BillingCycle = order.BillingCycle,
                AmountPaid = order.FinalAmount,
                Status = "Active",
                StartDate = now,
                EndDate = endDate,
                IsComplimentary = order.FinalAmount == 0,
                ActivatedBy = activatedBy
            };
            _db.PartnerSubscriptions.Add(sub);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("PartnerSubscription", sub.Id, "SubscriptionActivated",
                activatedBy.StartsWith("admin") ? "admin" : "system", partnerId);
            return sub;
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
                sub.StartDate, sub.EndDate, sub.AmountPaid, sub.Status, null);
        }
    }

// ─────────────────────────────────────────────
// 10. ENTITLEMENT SERVICE
// ─────────────────────────────────────────────

    public interface IEntitlementService
    {
        Task<bool> CanAccessContentAsync(int partnerId, int contentItemId);
        Task<string?> GetRequiredPlanNameAsync(int contentItemId);
    }

    public class EntitlementService : IEntitlementService
    {
        private readonly AppDbContext _db;
        public EntitlementService(AppDbContext db) => _db = db;

        public async Task<bool> CanAccessContentAsync(int partnerId, int contentItemId)
        {
            var content = await _db.ContentItems.FindAsync(contentItemId);
            if (content == null) return false;
            if (!content.RequiresPlanAccess) return true; // free content

            var sub = await _db.PartnerSubscriptions
                .FirstOrDefaultAsync(s => s.PartnerId == partnerId
                    && s.Status == "Active" && s.EndDate > DateTime.UtcNow);
            if (sub == null) return false;

            // Check plan entitlement
            var entitled = await _db.PlanEntitlements
                .AnyAsync(e => e.PlanId == sub.PlanId && (
                    e.AllContent ||
                    e.ContentItemId == contentItemId ||
                    (e.CategoryId != null && e.CategoryId == content.CategoryId)
                ));
            return entitled;
        }

        public async Task<string?> GetRequiredPlanNameAsync(int contentItemId)
        {
            var ent = await _db.PlanEntitlements
                .Include(e => e.Plan)
                .Where(e => e.ContentItemId == contentItemId || e.AllContent)
                .FirstOrDefaultAsync();
            return ent?.Plan?.Name;
        }
    }

    // ─────────────────────────────────────────────
    // 12. AUDIT SERVICE
    // ─────────────────────────────────────────────

        public interface IAuditService
        {
            Task LogAsync(string entityType, int entityId, string action,
                string actorType, int? actorId = null,
                string? oldValue = null, string? newValue = null);
        }

        public class AuditService : IAuditService
        {
            private readonly AppDbContext _db;
            private readonly IHttpContextAccessor _http;
            public AuditService(AppDbContext db, IHttpContextAccessor http)
            { _db = db; _http = http; }

            public async Task LogAsync(string entityType, int entityId, string action,
                string actorType, int? actorId = null,
                string? oldValue = null, string? newValue = null)
            {
                _db.AuditLogs.Add(new AuditLog
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    Action = action,
                    ActorType = actorType,
                    ActorId = actorId,
                    OldValue = oldValue,
                    NewValue = newValue,
                    IpAddress = _http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }
        }
 

