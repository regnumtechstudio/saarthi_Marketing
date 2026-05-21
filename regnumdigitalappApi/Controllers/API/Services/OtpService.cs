using RegnumDigital.API.Data;
using RegnumDigital.API.Models;

namespace RegnumDigital.API.Services;

public class OtpService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly EmailService _email;
    public OtpService(AppDbContext db, IConfiguration cfg, EmailService email)
    {
        _db = db; _cfg = cfg;
        _email = email;

    }

    public async Task<string> GenerateAndSaveAsync(string identifier, string userType, string? displayName = null)
    {
        try
        {
            // Invalidate old OTPs
            var old = _db.OtpStore
                .Where(o => o.Identifier == identifier && o.UserType == userType && !o.IsUsed);
            foreach (var o in old) o.IsUsed = true;

            var otp = new Random().Next(1000, 9999).ToString();
            _db.OtpStore.Add(new OtpStore
            {
                Identifier = identifier,
                OtpCode = otp,
                UserType = userType,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            });
            await _db.SaveChangesAsync();

            // Always print to console (works even without email config)
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n{"=",40}");
            Console.WriteLine($"  OTP for {identifier} ({userType}): {otp}");
            Console.WriteLine($"  Expires in 10 minutes");
            Console.WriteLine($"{"=",40}\n");
            Console.ResetColor();

            // Fire-and-forget email (never blocks login, never crashes)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _email.SendOtpEmailAsync_New(identifier, displayName ?? identifier, otp);
                }
                catch
                {
                    /* swallow - email is non-critical */
                }
            });

            return otp;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[OTP ERROR] {ex.Message}");
            Console.ResetColor();
            throw;
        }
    }

    public async Task<bool> VerifyAsync(string identifier, string code, string userType)
    {
        var record = _db.OtpStore
            .Where(o => o.Identifier == identifier
                     && o.OtpCode == code
                     && o.UserType == userType
                     && !o.IsUsed
                     && o.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefault();

        if (record == null) return false;

        record.IsUsed = true;
        await _db.SaveChangesAsync();
        return true;
    }
}
  

