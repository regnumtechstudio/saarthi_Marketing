using Microsoft.EntityFrameworkCore;
using RegnumDigital.API.Models;
//using static modelObject.Models_additions;

namespace RegnumDigital.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<OtpStore> OtpStore => Set<OtpStore>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<AmcMaster> AmcMasters => Set<AmcMaster>();
    public DbSet<ContentAsset> ContentAssets => Set<ContentAsset>();
    public DbSet<PartnerCollection> PartnerCollections => Set<PartnerCollection>();
    public DbSet<PartnerActivity> PartnerActivities => Set<PartnerActivity>();
    public DbSet<PartnerCobrand> PartnerCobrands => Set<PartnerCobrand>();
    public DbSet<EventRegistration> EventRegistrations => Set<EventRegistration>();
    public DbSet<LmsCourse> LmsCourses => Set<LmsCourse>();
    public DbSet<LmsLesson> LmsLessons => Set<LmsLesson>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailConfig> EmailConfigs => Set<EmailConfig>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<FcmConfig> FcmConfigs { get; set; }
    public DbSet<FcmToken> FcmTokens { get; set; }
    public DbSet<PushNotification> PushNotifications { get; set; }
    public DbSet<BackOfficeUser> BackOfficeUsers { get; set; }
    public DbSet<BoPermission> BoPermissions { get; set; }

    public DbSet<ContentItem> ContentItems { get; set; }
    public DbSet<CobrandProfile> CobrandProfiles { get; set; }
    public DbSet<Collection> Collections { get; set; }
    public DbSet<CollectionItem> CollectionItems { get; set; }
    public DbSet<ActivityLog> ActivityLogs { get; set; }

    // New tables
    
    public DbSet<CheckoutOrder> CheckoutOrders { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<PartnerSubscription> PartnerSubscriptions { get; set; }
    public DbSet<PlanEntitlement> PlanEntitlements { get; set; }
    public DbSet<PromoCodeUsage> PromoCodeUsages { get; set; }
    public DbSet<ContentPlanMapping> ContentPlanMappings { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Self-referencing Category
        mb.Entity<Category>()
            .HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique indexes
        mb.Entity<PartnerCollection>()
            .HasIndex(pc => new { pc.PartnerId, pc.AssetId }).IsUnique();
        mb.Entity<EventRegistration>()
            .HasIndex(er => new { er.EventId, er.PartnerId }).IsUnique();
        mb.Entity<PartnerCobrand>()
            .HasIndex(cb => cb.PartnerId).IsUnique();

        //mb.Entity<Partner>(e =>
        //{
        //    e.ToTable("Partners");
        //    e.HasKey(x => x.Id);
        //    e.Property(x => x.Status).HasConversion<string>();
        //    e.HasIndex(x => x.Email).IsUnique();
        //});

        // ── Plans ─────────────────────────────────────────
        //mb.Entity<Plan>(e => {
        //    e.ToTable("Plans");
        //    e.HasKey(x => x.Id);
        //    e.Property(x => x.FeaturesJson).HasColumnType("json");
        //});

        // ── PromoCodes ────────────────────────────────────
        //mb.Entity<PromoCode>(e => {
        //    e.ToTable("PromoCodes");
        //    e.HasKey(x => x.Id);
        //    e.HasIndex(x => x.Code).IsUnique();
        //    e.Property(x => x.DiscountType).HasConversion<string>();
        //    e.Property(x => x.ApplicablePlansJson).HasColumnType("json");
        //});

        // ── CheckoutOrders ────────────────────────────────
        mb.Entity<CheckoutOrder>(e => {
            e.ToTable("CheckoutOrders");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => x.GatewayOrderId);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.BillingCycle).HasConversion<string>();
            e.HasOne(x => x.Partner).WithMany().HasForeignKey(x => x.PartnerId);
            e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId);
            e.HasOne(x => x.PromoCode).WithMany().HasForeignKey(x => x.PromoCodeId);
        });

        // ── Payments ──────────────────────────────────────
        mb.Entity<Payment>(e => {
            e.ToTable("Payments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.GatewayPaymentId).IsUnique(); // idempotency
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.CheckoutOrder).WithOne(o => o.Payment)
                .HasForeignKey<Payment>(x => x.CheckoutOrderId);
        });

        // ── PartnerSubscriptions ──────────────────────────
        //mb.Entity<PartnerSubscription>(e => {
        //    e.ToTable("PartnerSubscriptions");
        //    e.HasKey(x => x.Id);
        //    e.Property(x => x.Status).HasConversion<string>();
        //    e.Property(x => x.BillingCycle).HasConversion<string>();
        //    e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId);
        //    e.HasOne(x => x.Partner).WithMany(p => p.Subscriptions)
        //        .HasForeignKey(x => x.PartnerId);
        //});

        // ── PlanEntitlements ──────────────────────────────
        //mb.Entity<PlanEntitlement>(e => {
        //    e.ToTable("PlanEntitlements");
        //    e.HasKey(x => x.Id);
        //    e.HasOne(x => x.Plan).WithMany(p => p.Entitlements)
        //        .HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        //});

        // ── PromoCodeUsages ───────────────────────────────
        mb.Entity<PromoCodeUsage>(e => {
            e.ToTable("PromoCodeUsages");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.PromoCode).WithMany().HasForeignKey(x => x.PromoCodeId);
            e.HasOne(x => x.Partner).WithMany().HasForeignKey(x => x.PartnerId);
        });

        // ── AuditLogs ─────────────────────────────────────
        mb.Entity<AuditLog>(e => {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.OldValue).HasColumnType("json");
            e.Property(x => x.NewValue).HasColumnType("json");
            e.Property(x => x.ActorType).HasConversion<string>();
        });

        // ── CobrandProfiles ───────────────────────────────
        //mb.Entity<CobrandProfile>(e => {
        //    e.ToTable("CobrandProfiles");
        //    e.HasKey(x => x.Id);
        //    e.HasIndex(x => x.PartnerId).IsUnique();
        //    e.HasOne(x => x.Partner).WithOne(p => p.CobrandProfile)
        //        .HasForeignKey<CobrandProfile>(x => x.PartnerId);
        //});
    }
}
