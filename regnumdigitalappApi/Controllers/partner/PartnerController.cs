using Microsoft.AspNetCore.Mvc;
using Dapper;
using MySql.Data.MySqlClient;
using Org.BouncyCastle.Crypto.Generators;
using static regnumdigitalappApi.Model.modelObject.RequestDTO;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace regnumdigitalappApi.Controllers.partner
{
    [Route("api/[controller]")]
    [ApiController]
    public class PartnerController : ControllerBase
    {

        private readonly MySqlConnection _db;
        public PartnerController(MySqlConnection db) { _db = db; }

        // GET /api/partner/summary
        [HttpGet("summary")]
        public async Task<IActionResult> Summary()
        {
            var userId = long.Parse(User.FindFirst("sub")!.Value);
            var row = await _db.QueryFirstOrDefaultAsync(
                @"SELECT p.aum_total AS aumTotal, p.client_count AS clientCount,
                     p.coins_balance AS coinsBalance, p.tier, p.referral_link AS referralLink,
                     COALESCE(SUM(c.net_amount),0) AS commissionThisMonth
              FROM partners p
              LEFT JOIN commissions c ON c.partner_id=p.id
                AND DATE_FORMAT(c.created_at,'%Y-%m')=DATE_FORMAT(NOW(),'%Y-%m')
              WHERE p.user_id=@userId
              GROUP BY p.id", new { userId });
            return Ok(row);
        }

        // GET /api/partner/clients
        [HttpGet("clients")]
        public async Task<IActionResult> Clients([FromQuery] int page = 1, [FromQuery] string search = "")
        {
            var userId = long.Parse(User.FindFirst("sub")!.Value);
            var partner = await _db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id FROM partners WHERE user_id=@userId", new { userId });
            if (partner == null) return NotFound();

            var offset = (page - 1) * 20;
            var rows = await _db.QueryAsync(
                @"SELECT vcs.* FROM vw_client_summary vcs
              JOIN client_partner_map cpm ON cpm.client_id=vcs.client_id
              WHERE cpm.partner_id=@partnerId
                AND (@search='' OR vcs.full_name LIKE CONCAT('%',@search,'%') OR vcs.pan LIKE CONCAT('%',@search,'%'))
              LIMIT 20 OFFSET @offset",
                new { partnerId = (long)partner.id, search, offset });
            return Ok(new { clients = rows, page, total = rows.Count() });
        }

        // POST /api/partner/clients/onboard
        [HttpPost("clients/onboard")]
        public async Task<IActionResult> OnboardClient([FromBody] OnboardClientRequest req)
        {
            var userId = long.Parse(User.FindFirst("sub")!.Value);
            var partner = await _db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id, partner_code FROM partners WHERE user_id=@userId", new { userId });
            if (partner == null) return Forbid();

            // Create user
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

            // Init KYC
            await _db.ExecuteAsync(
                "INSERT INTO kyc_details (user_id,kyc_status) VALUES (@clientId,'pending')", new { clientId });

            // Map to partner
            await _db.ExecuteAsync(
                "INSERT INTO client_partner_map (client_id,partner_id) VALUES (@clientId,@partnerId)",
                new { clientId, partnerId = (long)partner.id });

            // Award coins to partner
            await _db.ExecuteAsync(
                "UPDATE partners SET coins_balance=coins_balance+100, client_count=client_count+1 WHERE id=@id",
                new { id = (long)partner.id });
            await _db.ExecuteAsync(
                "INSERT INTO partner_coins_ledger (partner_id,txn_type,coins,reason) VALUES (@id,'credit',100,'New client onboarded')",
                new { id = (long)partner.id });

            return Ok(new { clientId, message = "Client onboarded! 100 coins credited." });
        }

        // POST /api/partner/clients/{clientId}/risk-profile
        [HttpPost("clients/{clientId}/risk-profile")]
        public async Task<IActionResult> RiskProfile(long clientId, [FromBody] RiskAnswers req)
        {
            var score = (req.q1 + req.q2 + req.q3 + req.q4 + req.q5);
            var label = score <= 9 ? "Conservative" :
                        score <= 16 ? "Moderate" :
                        score <= 23 ? "Mod Aggressive" : "Aggressive";

            var portfolio = await _db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id FROM kuber_portfolios WHERE @score BETWEEN min_risk_score AND max_risk_score LIMIT 1",
                new { score });

            await _db.ExecuteAsync(
                @"INSERT INTO risk_profiles (user_id,score,profile_label,recommended_portfolio_id,q1_answer,q2_answer,q3_answer,q4_answer,q5_answer)
              VALUES (@clientId,@score,@label,@portId,@q1,@q2,@q3,@q4,@q5)",
                new { clientId, score, label, portId = (long?)portfolio?.id, req.q1, req.q2, req.q3, req.q4, req.q5 });

            return Ok(new { score, profileLabel = label, recommendedPortfolioId = (long?)portfolio?.id });
        }

        // GET: api/<PartnerController>
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET api/<PartnerController>/5
        [HttpGet("{id}")]
        public string Get(int id)
        {
            return "value";
        }

        // POST api/<PartnerController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<PartnerController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<PartnerController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
