using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Dapper;
using static regnumdigitalappApi.Model.modelObject.RequestDTO;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace regnumdigitalappApi.Controllers.sip
{
    [Route("api/[controller]")]
    [ApiController]
    public class SipController : ControllerBase
    {

        private readonly MySqlConnection _db;
        public SipController(MySqlConnection db) { _db = db; }

        // GET /api/sip/active
        [HttpGet("active")]
        public async Task<IActionResult> Active()
        {
            var userId = long.Parse(User.FindFirst("sub")!.Value);
            var rows = await _db.QueryAsync(
                @"SELECT sp.id AS sipId, ms.scheme_name AS schemeName,
                     sp.sip_amount AS amount, sp.frequency, sp.sip_date AS sipDate,
                     sp.next_sip_date AS nextDate, sp.status
              FROM sip_plans sp
              JOIN mf_scheme_master ms ON ms.id=sp.scheme_id
              WHERE sp.user_id=@userId AND sp.status IN ('active','paused')
              ORDER BY sp.next_sip_date", new { userId });
            return Ok(rows);
        }

        // POST /api/sip/create
        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] SipCreateRequest req)
        {
            var userId = long.Parse(User.FindFirst("sub")!.Value);
            var sipId = await _db.ExecuteScalarAsync<long>(
                @"INSERT INTO sip_plans (user_id, scheme_id, sip_amount, frequency, sip_date,
                start_date, end_date, next_sip_date, status)
              VALUES (@userId, @schemeId, @amount, @frequency, @sipDate,
                      @startDate, @endDate, @nextSipDate, 'active');
              SELECT LAST_INSERT_ID();",
                new
                {
                    userId,
                    req.schemeId,
                    req.amount,
                    req.frequency,
                    req.sipDate,
                    req.startDate,
                    req.endDate,
                    nextSipDate = req.startDate
                });
            return Ok(new { sipId, message = "SIP created successfully" });
        }

        // PATCH /api/sip/{sipId}/pause
        [HttpPatch("{sipId}/pause")]
        public async Task<IActionResult> Pause(long sipId, [FromBody] PauseRequest req)
        {
            var userId = long.Parse(User.FindFirst("sub")!.Value);
            await _db.ExecuteAsync(
                "UPDATE sip_plans SET status='paused', pause_reason=@reason WHERE id=@sipId AND user_id=@userId",
                new { sipId, userId, reason = req.reason });
            return Ok(new { message = "SIP paused" });
        }

        // DELETE /api/sip/{sipId}
        [HttpDelete("{sipId}")]
        public async Task<IActionResult> Cancel(long sipId)
        {
            var userId = long.Parse(User.FindFirst("sub")!.Value);
            await _db.ExecuteAsync(
                "UPDATE sip_plans SET status='cancelled' WHERE id=@sipId AND user_id=@userId",
                new { sipId, userId });
            return Ok(new { message = "SIP cancelled" });
        }

        // GET: api/<SipController>
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET api/<SipController>/5
        [HttpGet("{id}")]
        public string Get(int id)
        {
            return "value";
        }

        // POST api/<SipController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<SipController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<SipController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
