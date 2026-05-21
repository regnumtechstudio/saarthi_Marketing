using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using RegnumDigital.API.Data;
using RegnumDigital.API.DTOs;
using RegnumDigital.API.Models;
using RegnumDigital.API.Services;
using System.Data.Entity;
using System.Linq;
using System.Text.Json;
using static RegnumDigital.API.Services.JwtService;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace regnumdigitalappApi.Controllers.API.Controllers
{ 
    // ── Helper base ──
    [ApiController]
    public abstract class BaseController : ControllerBase
    {
        protected int GetPartnerId() =>
            int.Parse(User.FindFirst("sub")?.Value ?? "0");
        protected string GetScope() =>
            User.FindFirst("scope")?.Value ?? "";
        protected IActionResult Err(string msg, int code = 400) =>
            StatusCode(code, new { error = msg });
        protected IActionResult Ok2(object data) =>
            Ok(new { data });
    }

    // ============================================================
    // PLANS CONTROLLER (public)
    // ============================================================
    [Route("api/plans")]
    public class PlansController : BaseController
    {
        private readonly AppDbContext _db;
        public PlansController(AppDbContext db) => _db = db;

        [HttpGet("active")]
        public async Task<IActionResult> GetActivePlans()
        {
            var plans = await _db.Plans
                .Where(p => p.IsActive && p.IsVisible)
                .OrderBy(p => p.SortOrder)
                .ToListAsync();

            var dtos = plans.Select(p => new PlanDto(
                p.Id, p.Name, p.MonthlyPrice, p.YearlyPrice, p.SortOrder, p.FeaturesJson,p.IsPopular)).ToList();
            //    : [],p.IsPopular
            //(bool)p.IsPopular, p.SortOrder,
            //p.FeaturesJson != null
            //    ? JsonSerializer.Deserialize<string[]>(p.FeaturesJson) ?? []
            //    : []
            //)).ToList(); 

            return Ok(dtos);
        }

        [HttpGet("visible-promos")]
        public async Task<IActionResult> GetVisiblePromos([FromQuery] int planId)
        {
            var now = DateTime.UtcNow;
            var promos = await _db.PromoCodes
                .Where(p => p.IsActive
                    && p.IsVisibleOnCheckout
                    && (p.ExpiresAt == null || p.ExpiresAt > now)
                    && (p.MaxUses == null || p.CurrentUses < p.MaxUses))
                .ToListAsync();

            // Filter by plan applicability
            var applicable = promos.Where(p => {
                if (string.IsNullOrEmpty(p.ApplicablePlansJson)) return true;
                var ids = JsonSerializer.Deserialize<int[]>(p.ApplicablePlansJson) ?? null; //[]
                return ids.Length == 0 || ids.Contains(planId);
            });

            var dtos = applicable.Select(p => new VisiblePromoDto(
                p.Code, p.VisibleDescription ?? "", p.DiscountType, p.DiscountValue
            )).ToList();

            return Ok2(dtos);
        }
    }

    // ============================================================
    // CHECKOUT CONTROLLER
    // ============================================================
    [Route("api/checkout")]
    [Authorize]
    public class CheckoutController : BaseController
    {
        private readonly ICheckoutService _checkout;
        public CheckoutController(ICheckoutService checkout) => _checkout = checkout;

        [HttpPost("apply-promo")]
        public async Task<IActionResult> ApplyPromo([FromBody] ApplyPromoRequest req)
        {
            try
            {
                var result = await _checkout.ApplyPromoAsync(GetPartnerId(), req);
                return Ok2(result);
            }
            catch (BusinessException ex) { return Err(ex.Message); }
        }

        [HttpPost("create-order")]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest req)
        {
            try
            {
                var result = await _checkout.CreateOrderAsync(GetPartnerId(), req);
                return Ok2(result);
            }
            catch (BusinessException ex) { return Err(ex.Message); }
        }

        [HttpPost("activate-zero")]
        public async Task<IActionResult> ActivateZero([FromBody] ActivateZeroRequest req)
        {
            try
            {
                var result = await _checkout.ActivateZeroOrderAsync(GetPartnerId(), req.OrderId);
                return Ok2(result);
            }
            catch (BusinessException ex) { return Err(ex.Message); }
        }
    }

    // ============================================================
    // PAYMENT CONTROLLER
    // ============================================================
    [Route("api/payment")]
    public class PaymentController : BaseController
    {
        private readonly AppDbContext _db;
        private readonly IPaymentService _payment;
        private readonly ISubscriptionService _subscription;
        private readonly IJwtService _jwt;
        private readonly IAuditService _audit;

        public PaymentController(AppDbContext db, IPaymentService payment,
            ISubscriptionService subscription, IJwtService jwt, IAuditService audit)
        {
            _db = db; _payment = payment; _subscription = subscription;
            _jwt = jwt; _audit = audit;
        }

        [HttpPost("verify")]
        [Authorize]
        public async Task<IActionResult> VerifyPayment([FromBody] VerifyPaymentRequest req)
        {
            // 1. Verify signature — NEVER activate without this
            if (!_payment.VerifyPaymentSignature(
                req.RazorpayOrderId, req.RazorpayPaymentId, req.RazorpaySignature))
                return Err("Invalid payment signature.", 400);

            var partnerId = GetPartnerId();

            // 2. Idempotency: check if already processed
            var existing = await _db.Payments
                .FirstOrDefaultAsync(p => p.GatewayPaymentId == req.RazorpayPaymentId);
            if (existing?.Status == "Captured")
            {
                // Already captured — return token (idempotent)
                var partner2 = await _db.Partners.FindAsync(partnerId)!;
                var activeSub = await _db.PartnerSubscriptions
                    .Where(s => s.PartnerId == partnerId && s.Status == "Active").FirstOrDefaultAsync();
                var tok = _jwt.GenerateFullToken(partnerId, partner2!.FullName, partner2.Email,
                    partner2.RoleId, activeSub?.PlanId, partner2.CobrandSetupDone);
                return Ok2(new
                {
                    token = tok,
                    partnerId,
                    //name = partner2.Name,
                    name = partner2.FullName,
                    cobrandSetupDone = partner2.CobrandSetupDone,
                    roleId = partner2.RoleId,
                    userType = "partner"
                });
            }

            // 3. Find order
            var order = await _db.CheckoutOrders
                .Include(o => o.Plan)
                .FirstOrDefaultAsync(o => o.GatewayOrderId == req.RazorpayOrderId
                    && o.PartnerId == partnerId)
                ?? throw new Exception("Order not found.");

            // 4. Record payment
            var payment = new Payment
            {
                CheckoutOrderId = order.Id,
                PartnerId = partnerId,
                GatewayPaymentId = req.RazorpayPaymentId,
                GatewayOrderId = req.RazorpayOrderId,
                GatewaySignature = req.RazorpaySignature,
                Amount = order.FinalAmount,
                Status = "Captured",
                CapturedAt = DateTime.UtcNow
            };
            _db.Payments.Add(payment);

            // 5. Record promo usage
            if (order.PromoCodeId.HasValue)
            {
                _db.PromoCodeUsages.Add(new PromoCodeUsage
                {
                    PromoCodeId = order.PromoCodeId.Value,
                    PartnerId = partnerId,
                    CheckoutOrderId = order.Id
                });
                var promo = await _db.PromoCodes.FindAsync(order.PromoCodeId.Value);
                if (promo != null) promo.CurrentUses++;
            }

            // 6. Activate subscription
            var sub = await _subscription.ActivateAsync(partnerId, order, "system");

            // 7. Update order + partner
            order.Status = "Activated";
            order.CompletedAt = DateTime.UtcNow;
            var partner = await _db.Partners.FindAsync(partnerId)!;
            partner!.Status = "Active";
            partner.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _audit.LogAsync("Payment", payment.Id, "PaymentCaptured", "system", partnerId);

            var token = _jwt.GenerateFullToken(partnerId, partner.FullName, partner.Email,
                partner.RoleId, sub.PlanId, partner.CobrandSetupDone);

            return Ok2(new
            {
                token,
                partnerId,
              //  name = partner.Name,
                name = partner.FullName,
                cobrandSetupDone = partner.CobrandSetupDone,
                roleId = partner.RoleId,
                mobile = partner.Mobile,
                userType = "partner"
            });
        }

        // Razorpay server-to-server webhook (no auth, verify signature instead)
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            // CRITICAL: Read raw body BEFORE model binding
            Request.EnableBuffering();
            var rawBody = await new StreamReader(Request.Body).ReadToEndAsync();
            Request.Body.Position = 0;

            var signature = Request.Headers["X-Razorpay-Signature"].ToString();
            if (!_payment.VerifyWebhookSignature(rawBody, signature))
                return Ok(); // Always 200 to Razorpay, but don't process

            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            var evt = root.GetProperty("event").GetString();

            if (evt != "payment.captured") return Ok();

            var paymentEl = root.GetProperty("payload").GetProperty("payment").GetProperty("entity");
            var paymentId = paymentEl.GetProperty("id").GetString()!;
            var rzpOrderId = paymentEl.GetProperty("order_id").GetString()!;
            var amountPaise = paymentEl.GetProperty("amount").GetInt64();

            // Idempotency: if already processed, do nothing
            var existingPay = await _db.Payments
                .FirstOrDefaultAsync(p => p.GatewayPaymentId == paymentId);
            if (existingPay != null) return Ok();

            // Find order
            var order = await _db.CheckoutOrders
                .FirstOrDefaultAsync(o => o.GatewayOrderId == rzpOrderId);
            if (order == null) return Ok();

            // Record payment
            var payment = new Payment
            {
                CheckoutOrderId = order.Id,
                PartnerId = order.PartnerId,
                GatewayPaymentId = paymentId,
                GatewayOrderId = rzpOrderId,
                Amount = amountPaise / 100m,
                Status = "Captured",
                IsWebhookVerified = true,
                RawWebhookPayload = rawBody,
                CapturedAt = DateTime.UtcNow
            };
            _db.Payments.Add(payment);

            if (order.PromoCodeId.HasValue)
            {
                _db.PromoCodeUsages.Add(new PromoCodeUsage
                {
                    PromoCodeId = order.PromoCodeId.Value,
                    PartnerId = order.PartnerId,
                    CheckoutOrderId = order.Id
                });
                var promo = await _db.PromoCodes.FindAsync(order.PromoCodeId.Value);
                if (promo != null) promo.CurrentUses++;
            }

            await _subscription.ActivateAsync(order.PartnerId, order, "webhook");
            order.Status = "Activated";
            order.CompletedAt = DateTime.UtcNow;
            var partner = await _db.Partners.FindAsync(order.PartnerId)!;
            if (partner != null) partner.Status = "Active";
            await _db.SaveChangesAsync();

            await _audit.LogAsync("Payment", payment.Id, "WebhookCaptured", "system", order.PartnerId);
            return Ok();
        }
    }

    // ============================================================
    // PARTNER SUBSCRIPTION CONTROLLER
    // ============================================================
    [Route("api/partner")]
    [Authorize]
    public class PartnerSubscriptionController : BaseController
    {
        private readonly ISubscriptionService _subscription;
        private readonly AppDbContext _db;

        public PartnerSubscriptionController(ISubscriptionService subscription, AppDbContext db)
        { _subscription = subscription; _db = db; }

        [HttpGet("subscription")]
        public async Task<IActionResult> GetSubscription()
        {
            var sub = await _subscription.GetActiveSubscriptionAsync(GetPartnerId());
            return Ok2(sub);
        }

        [HttpPost("cobrand/setup-done")]
        public async Task<IActionResult> MarkCobrandDone()
        {
            var partner = await _db.Partners.FindAsync(GetPartnerId());
            if (partner == null) return NotFound();
            partner.CobrandSetupDone = true;
            await _db.SaveChangesAsync();
            return Ok2(new { message = "Cobrand setup marked complete." });
        }
    }

    // ============================================================
    // ADMIN — PLANS CONTROLLER
    // ============================================================
    [Route("api/admin/plans")]
    [Authorize(Policy = "AdminOnly")]
    public class AdminPlansController : BaseController
    {
        private readonly AppDbContext _db;
        public AdminPlansController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var plans = await _db.Plans.OrderBy(p => p.SortOrder).ToListAsync();
            return Ok2(plans.Select(p => new {
                p.Id,
                p.Name,
                p.MonthlyPrice,
                p.YearlyPrice,
                p.RoleId,
                p.IsPopular,
                p.IsVisible,
                p.IsActive,
                p.SortOrder,
                features = p.FeaturesJson != null
                    ? JsonSerializer.Deserialize<string[]>(p.FeaturesJson)
                    : Array.Empty<string>(),
                roleName = (string?)null // join role name if needed
            }));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SavePlanRequest req)
        {
            var plan = new Plan
            {
                Name = req.Name,
                MonthlyPrice = req.MonthlyPrice,
                YearlyPrice = req.YearlyPrice,
                RoleId = req.RoleId,
                IsPopular = req.IsPopular,
                IsVisible = req.IsVisible,
                IsActive = true,
                SortOrder = req.SortOrder,
                FeaturesJson = JsonSerializer.Serialize(req.Features)
            };
            _db.Plans.Add(plan);
            await _db.SaveChangesAsync();
            return Ok2(new { plan.Id, message = "Plan created." });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] SavePlanRequest req)
        {
            var plan = await _db.Plans.FindAsync(id);
            if (plan == null) return NotFound();
            plan.Name = req.Name;
            plan.MonthlyPrice = req.MonthlyPrice;
            plan.YearlyPrice = req.YearlyPrice;
            plan.RoleId = req.RoleId;
            plan.IsPopular = req.IsPopular;
            plan.IsVisible = req.IsVisible;
            plan.SortOrder = req.SortOrder;
            plan.FeaturesJson = JsonSerializer.Serialize(req.Features);
            await _db.SaveChangesAsync();
            return Ok2(new { message = "Plan updated." });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var plan = await _db.Plans.FindAsync(id);
            if (plan == null) return NotFound();
            plan.IsActive = false;
            await _db.SaveChangesAsync();
            return Ok2(new { message = "Plan deactivated." });
        }
    }

    // ============================================================
    // ADMIN — PROMOS CONTROLLER
    // ============================================================
    [Route("api/admin/promos")]
    [Authorize(Policy = "AdminOnly")]
    public class AdminPromosController : BaseController
    {
        private readonly AppDbContext _db;
        public AdminPromosController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var promos = await _db.PromoCodes.OrderByDescending(p => p.Id).ToListAsync();
            return Ok2(promos.Select(p => new {
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
            if (string.IsNullOrWhiteSpace(req.Code))
                return Err("Promo code is required.");
            if (req.DiscountType == "percentage" && req.DiscountValue > 100)
                return Err("Percentage discount cannot exceed 100.");

            var exists = await _db.PromoCodes.AnyAsync(p => p.Code == req.Code.ToUpper());
            if (exists) return Err("Promo code already exists.");

            DateTime? expiresAt = null;
      if (!string.IsNullOrEmpty(Convert.ToString(req.ExpiresAt))) ;
               // expiresAt = DateTime.Parse(req.ExpiresAt).ToUniversalTime();

            var promo = new PromoCode
            {
                Code = req.Code.ToUpper(),
                DiscountType = req.DiscountType,
                DiscountValue = req.DiscountValue,
                ExpiresAt = expiresAt,
                MaxUses = req.MaxUses,
                PerUserLimit = req.PerUserLimit,
                IsActive = true,
                IsVisibleOnCheckout = req.IsVisibleOnCheckout,
                VisibleDescription = req.VisibleDescription,
                //ApplicablePlansJson = req.ApplicablePlanIds?.Length > 0
                //    ? JsonSerializer.Serialize(req.ApplicablePlanIds)
                //    : null
            };
            _db.PromoCodes.Add(promo);
            await _db.SaveChangesAsync();
            return Ok2(new { promo.Id, message = "Promo created." });
        }

        [HttpPut("{id}/deactivate")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var promo = await _db.PromoCodes.FindAsync(id);
            if (promo == null) return NotFound();
            promo.IsActive = false;
            await _db.SaveChangesAsync();
            return Ok2(new { message = "Deactivated." });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var promo = await _db.PromoCodes.FindAsync(id);
            if (promo == null) return NotFound();
            _db.PromoCodes.Remove(promo);
            await _db.SaveChangesAsync();
            return Ok2(new { message = "Deleted." });
        }
    }

    // ============================================================
    // ADMIN — SUBSCRIPTIONS CONTROLLER
    // ============================================================
    [Route("api/admin/subscriptions")]
    [Authorize(Policy = "AdminOnly")]
    public class AdminSubscriptionsController : BaseController
    {
        private readonly AppDbContext _db;
        private readonly IAuditService _audit;

        public AdminSubscriptionsController(AppDbContext db, IAuditService audit)
        { _db = db; _audit = audit; }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] string? search,
            [FromQuery] string? status,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            var q = _db.PartnerSubscriptions
                .Include(s => s.Plan)
                .Include(s => s.Partner)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
                q = q.Where(s => s.Status == status);

            if (!string.IsNullOrEmpty(search))
                q = q.Where(s =>
                    //s.Partner!.Name.Contains(search) ||
                    s.Partner!.FullName.Contains(search) ||
                    s.Partner.Email.Contains(search));

            var items = await q
                .OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new {
                    s.Id,
                    s.Status,
                    s.BillingCycle,
                    s.AmountPaid,
                    s.StartDate,
                    s.EndDate,
                    s.IsComplimentary,
                    planName = s.Plan!.Name,
                    //partnerName = s.Partner!.Name,
                    partnerName = s.Partner!.FullName,
                    partnerEmail = s.Partner.Email
                })
                .ToListAsync();

            return Ok2(items);
        }

        [HttpPost("manual-activate")]
        public async Task<IActionResult> ManualActivate([FromBody] ManualActivateRequest req)
        {
            var partner = await _db.Partners.FindAsync(req.PartnerId);
            if (partner == null) return Err("Partner not found.");
            var plan = await _db.Plans.FindAsync(req.PlanId);
            if (plan == null) return Err("Plan not found.");

            // Cancel existing active subscription
            var existing = await _db.PartnerSubscriptions
                .Where(s => s.PartnerId == req.PartnerId && s.Status == "Active")
                .FirstOrDefaultAsync();
            if (existing != null) { existing.Status = "Cancelled"; existing.CancelledAt = DateTime.UtcNow; }

            var now = DateTime.UtcNow;
            int days = req.DurationDays ?? (req.BillingCycle == "yearly" ? 365 : 30);
            var endDate = now.AddDays(days);

            // Create a dummy order for FK
            var dummyOrder = new CheckoutOrder
            {
                PartnerId = req.PartnerId,
                PlanId = req.PlanId,
                BillingCycle = req.BillingCycle,
                OriginalAmount = 0,
                FinalAmount = 0,
                Status = "Activated",
                CompletedAt = now
            };
            _db.CheckoutOrders.Add(dummyOrder);
            await _db.SaveChangesAsync();

            var sub = new PartnerSubscription
            {
                PartnerId = req.PartnerId,
                PlanId = req.PlanId,
                CheckoutOrderId = dummyOrder.Id,
                BillingCycle = req.BillingCycle,
                AmountPaid = 0,
                Status = "Active",
                StartDate = now,
                EndDate = endDate,
                IsComplimentary = true,
                Notes = req.Reason,
                ActivatedBy = "admin"
            };
            _db.PartnerSubscriptions.Add(sub);
            partner.Status = "Active";
            await _db.SaveChangesAsync();

            await _audit.LogAsync("PartnerSubscription", sub.Id,
                "ManualActivation", "admin", req.PartnerId,
                newValue: $"Plan={plan.Name}, Days={days}, Reason={req.Reason}");

            return Ok2(new { message = "Subscription activated.", subscriptionId = sub.Id });
        }

        [HttpPut("{id}/cancel")]
        public async Task<IActionResult> Cancel(int id)
        {
            var sub = await _db.PartnerSubscriptions.FindAsync(id);
            if (sub == null) return NotFound();
            sub.Status = "Cancelled";
            sub.CancelledAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("PartnerSubscription", id, "SubscriptionCancelled", "admin", sub.PartnerId);
            return Ok2(new { message = "Cancelled." });
        }
    }

    // ============================================================
    // ADMIN — PAYMENTS CONTROLLER
    // ============================================================
    [Route("api/admin/payments")]
    [Authorize(Policy = "AdminOnly")]
    public class AdminPaymentsController : BaseController
    {
        private readonly AppDbContext _db;
        public AdminPaymentsController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] string? search,
            [FromQuery] string? status,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            var q = _db.Payments
                .Include(p => p.CheckoutOrder)
                   // .ThenInclude(o => o.Plan)
                .Include(p => p.CheckoutOrder)
                 //   .ThenInclude(o => o.PromoCode)
                .Include(p => p.Partner)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
                q = q.Where(p => p.Status == status);

            if (!string.IsNullOrEmpty(search))
                q = q.Where(p =>
                    p.Partner!.FullName.Contains(search) ||
                    p.Partner.Email.Contains(search) ||
                    p.GatewayPaymentId.Contains(search));

            var items = await q
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new {
                    p.Id,
                    p.Amount,
                    p.Status,
                    p.GatewayPaymentId,
                    p.CreatedAt,
                    planName = p.CheckoutOrder!.Plan!.Name,
                    partnerName = p.Partner!.FullName,
                    partnerEmail = p.Partner.Email,
                    promoCode = p.CheckoutOrder.PromoCode != null ? p.CheckoutOrder.PromoCode.Code : null
                })
                .ToListAsync();

            return Ok2(items);
        }
    }
}

