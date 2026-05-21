using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using static regnumdigitalappApi.Model.modelObject.RequestDTO;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace regnumdigitalappApi.Controllers.admin
{
    [Route("api/v2/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly MySqlConnection _db;
        public AdminController(MySqlConnection db) { _db = db; }

        // GET /api/admin/dashboard
        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            var row = await _db.QueryFirstOrDefaultAsync(
                @"SELECT
                (SELECT COALESCE(SUM(aum_total),0) FROM partners) AS totalAum,
                (SELECT COUNT(*) FROM users WHERE role_id=1) AS clientsTotal,
                (SELECT COUNT(*) FROM partners WHERE is_active=1) AS activePartners,
                (SELECT COUNT(*) FROM sip_plans WHERE status='active') AS totalSips,
                (SELECT COUNT(*) FROM kyc_details WHERE kyc_status='submitted') AS pendingKyc,
                (SELECT COUNT(*) FROM orders WHERE DATE(placed_at)=CURDATE()) AS todayTransactions");
            return Ok(row);
        }

        // GET /api/admin/kyc/queue
        [HttpGet("kyc/queue")]
        public async Task<IActionResult> KycQueue([FromQuery] string status = "submitted")
        {
            var rows = await _db.QueryAsync (
                @"SELECT u.id AS userId, CONCAT(u.first_name,' ',u.last_name) AS clientName,
                     u.pan, u.mobile, k.kyc_status AS kycStatus, k.created_at AS submittedAt
              FROM kyc_details k JOIN users u ON u.id=k.user_id
              WHERE k.kyc_status=@status ORDER BY k.created_at ASC", new { status });
            return Ok(rows);
        }

        // POST /api/admin/kyc/{userId}/approve
        [HttpPost("kyc/{userId}/approve")]
        public async Task<IActionResult> KycApprove(long userId, [FromBody] ApproveRequest req)
        {
            var adminId = long.Parse(User.FindFirst("sub")!.Value);
            await _db.ExecuteAsync(
                "UPDATE kyc_details SET kyc_status='verified', verified_at=NOW(), verified_by=@adminId, remarks=@remarks WHERE user_id=@userId",
                new { userId, adminId, remarks = req.Remarks });

            // Audit log
            await _db.ExecuteAsync(
                "INSERT INTO audit_log (admin_user_id, action, entity_type, entity_id) VALUES (@adminId,'kyc_approved','user',@userId)",
                new { adminId, userId });

            // Notification to client
            await _db.ExecuteAsync(
                "INSERT INTO notifications (user_id, type, title, body) VALUES (@userId,'kyc_approved','KYC Approved 🎉','Your KYC has been verified. You can now start investing!')",
                new { userId });

            return Ok(new { message = "KYC approved" });
        }

        // POST /api/admin/kyc/{userId}/reject
        [HttpPost("kyc/{userId}/reject")]
        public async Task<IActionResult> KycReject(long userId, [FromBody] RejectRequest req)
        {
            var adminId = long.Parse(User.FindFirst("sub")!.Value);
            await _db.ExecuteAsync(
                "UPDATE kyc_details SET kyc_status='rejected', verified_by=@adminId, remarks=@reason WHERE user_id=@userId",
                new { userId, adminId, reason = req.Reason });
            await _db.ExecuteAsync(
                "INSERT INTO audit_log (admin_user_id, action, entity_type, entity_id) VALUES (@adminId,'kyc_rejected','user',@userId)",
                new { adminId, userId });
            return Ok(new { message = "KYC rejected" });
        }

        // POST /api/admin/clients   (create client)
        [HttpPost("clients")]
        public async Task<IActionResult> CreateClient([FromBody] CreateClientRequest req)
        {
            var adminId = long.Parse(User.FindFirst("sub")!.Value);
            var exists = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM users WHERE mobile=@mobile OR email=@email",
                new { req.mobile, req.email });
            if (exists > 0) return Conflict(new { message = "Mobile or email already exists" });

            var clientId = await _db.ExecuteScalarAsync<long>(
                @"INSERT INTO users (role_id,first_name,last_name,email,mobile,password_hash,pan)
              VALUES (1,@fn,@ln,@email,@mobile,@hash,@pan); SELECT LAST_INSERT_ID();",
                new
                {
                    fn = req.firstName,
                    ln = req.lastName,
                    req.email,
                    req.mobile,
                    hash = BCrypt.Net.BCrypt.HashPassword(req.mobile),
                    pan = req.pan
                });

            await _db.ExecuteAsync(
                "INSERT INTO kyc_details (user_id,kyc_status) VALUES (@clientId,'pending')", new { clientId });

            if (!string.IsNullOrEmpty(req.partnerCode))
            {
                var partner = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT id FROM partners WHERE partner_code=@code", new { code = req.partnerCode });
                if (partner != null)
                    await _db.ExecuteAsync(
                        "INSERT INTO client_partner_map (client_id,partner_id) VALUES (@clientId,@pid)",
                        new { clientId, pid = (long)partner.id });
            }

            await _db.ExecuteAsync(
                "INSERT INTO audit_log (admin_user_id, action, entity_type, entity_id) VALUES (@adminId,'client_created','user',@clientId)",
                new { adminId, clientId });

            return Ok(new { clientId, message = "Client created" });
        }

        // DELETE /api/admin/clients/{clientId}
        [HttpDelete("clients/{clientId}")]
        public async Task<IActionResult> DeactivateClient(long clientId)
        {
            var adminId = long.Parse(User.FindFirst("sub")!.Value);
            await _db.ExecuteAsync("UPDATE users SET is_active=0 WHERE id=@clientId", new { clientId });
            await _db.ExecuteAsync(
                "INSERT INTO audit_log (admin_user_id, action, entity_type, entity_id) VALUES (@adminId,'client_deactivated','user',@clientId)",
                new { adminId, clientId });
            return Ok(new { message = "Client deactivated" });
        }

        // POST /api/admin/partners   (add partner)
        [HttpPost("partners")]
        public async Task<IActionResult> AddPartner([FromBody] AddPartnerRequest req)
        {
            var adminId = long.Parse(User.FindFirst("sub")!.Value);
            var userId = await _db.ExecuteScalarAsync<long>(
                @"INSERT INTO users (role_id,first_name,last_name,email,mobile,password_hash)
              VALUES (2,@fn,@ln,@email,@mobile,@hash); SELECT LAST_INSERT_ID();",
                new
                {
                    fn = req.firstName,
                    ln = req.lastName,
                    req.email,
                    req.mobile,
                    hash = BCrypt.Net.BCrypt.HashPassword(req.mobile)
                });

            var pCode = $"RFR-{req.firstName.Substring(0, 2).ToUpper()}-{new Random().Next(100, 999)}";
            var refLink = $"https://app.regnum.co.in/register?ref={pCode}";

            var partnerId = await _db.ExecuteScalarAsync<long>(
                @"INSERT INTO partners (user_id, partner_code, arn_number, euin, tier, city, state, referral_link)
              VALUES (@userId, @pCode, @arn, @euin, @tier, @city, @state, @refLink);
              SELECT LAST_INSERT_ID();",
                new { userId, pCode, arn = req.arn, euin = req.euin, tier = req.tier, req.city, req.state, refLink });

            await _db.ExecuteAsync(
                "INSERT INTO audit_log (admin_user_id, action, entity_type, entity_id) VALUES (@adminId,'partner_created','partner',@partnerId)",
                new { adminId, partnerId });

            return Ok(new { partnerId, partnerCode = pCode, message = "Partner added" });
        }

        // POST /api/admin/nav/sync   (sync NAV from AMFI)
        [HttpPost("nav/sync")]
        public async Task<IActionResult> SyncNav()
        {
            var adminId = long.Parse(User.FindFirst("sub")!.Value);
            // Fetch from AMFI server-side (no CORS issue)
            using var http = new HttpClient();
            var txt = await http.GetStringAsync("https://www.amfiindia.com/spages/NAVAll.txt");
            var lines = txt.Trim().Split('\n');
            int updated = 0;

            foreach (var line in lines)
            {
                var p = line.Split(';');
                if (p.Length < 6) continue;
                if (!decimal.TryParse(p[4], out var nav)) continue;
                if (!DateTime.TryParse(p[5], out var navDate)) continue;
                var code = p[0].Trim();
                var name = p[3].Trim();
                if (!name.ToUpper().Contains("GROWTH") || name.ToUpper().Contains("DIVIDEND")) continue;

                var rows = await _db.ExecuteAsync(
                    "UPDATE mf_scheme_master SET nav_latest=@nav, nav_date=@navDate, last_nav_synced_at=NOW() WHERE scheme_code=@code",
                    new { nav, navDate, code });

                if (rows > 0)
                {
                    await _db.ExecuteAsync(
                        "INSERT INTO nav_history (scheme_id, nav_date, nav_value) SELECT id, @navDate, @nav FROM mf_scheme_master WHERE scheme_code=@code ON DUPLICATE KEY UPDATE nav_value=@nav",
                        new { nav, navDate, code });
                    updated++;
                }
            }

            await _db.ExecuteAsync(
                "INSERT INTO audit_log (admin_user_id, action, entity_type) VALUES (@adminId,'nav_synced','system')",
                new { adminId });

            return Ok(new { schemesUpdated = updated, timestamp = DateTime.Now });
        }

        // GET /api/admin/settings
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            var rows = await _db.QueryAsync("SELECT setting_key AS `key`, setting_value AS value FROM system_settings");
            return Ok(rows.ToDictionary(r => (string)r.key, r => (string)r.value));
        }

        // PUT /api/admin/settings
        [HttpPut("settings")]
        public async Task<IActionResult> SaveSettings([FromBody] Dictionary<string, string> settings)
        {
            foreach (var (key, val) in settings)
                await _db.ExecuteAsync(
                    "UPDATE system_settings SET setting_value=@val WHERE setting_key=@key", new { key, val });
            return Ok(new { message = "Settings saved" });
        }


        // GET: api/<AdminController>
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET api/<AdminController>/5
        [HttpGet("{id}")]
        public string Get(int id)
        {
            return "value";
        }

        // POST api/<AdminController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<AdminController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<AdminController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
