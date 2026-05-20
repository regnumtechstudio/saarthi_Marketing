using Microsoft.AspNetCore.Mvc;
using Dapper;
using MySql.Data.MySqlClient;
using static regnumdigitalappApi.Model.modelObject.RequestDTO;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace regnumdigitalappApi.Controllers.orders
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private readonly MySqlConnection _db;
        public OrdersController(MySqlConnection db) { _db = db; }

        // POST /api/orders/purchase
        // body: { schemeId, amount, paymentMode, bankAccountId }
        [HttpPost("purchase")]
        public async Task<IActionResult> Purchase([FromBody] PurchaseRequest req)
        {
            var userId = long.Parse(User.FindFirst("sub")!.Value);
            var ordRef = $"RD-ORD-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}";

            var orderId = await _db.ExecuteScalarAsync<long>(
                @"INSERT INTO orders (order_ref, user_id, scheme_id, order_type, amount, payment_mode, status)
              VALUES (@ordRef, @userId, @schemeId, 'purchase', @amount, @paymentMode, 'initiated');
              SELECT LAST_INSERT_ID();",
                new { ordRef, userId, req.schemeId, req.amount, req.paymentMode });

            // TODO: Call NSE INVEST / Cybrilla API to submit order

            return Ok(new
            {
                orderId,
                orderRef = ordRef,
                status = "initiated",
                message = "Order placed. Payment pending."
            });
        }

        // POST /api/orders/redemption
        [HttpPost("redemption")]
        public async Task<IActionResult> Redemption([FromBody] RedemptionRequest req)
        {
            var userId = long.Parse(User.FindFirst("sub")!.Value);
            var ordRef = $"RD-RDM-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}";

            var orderId = await _db.ExecuteScalarAsync<long>(
                @"INSERT INTO orders (order_ref, user_id, scheme_id, order_type, units, status)
              VALUES (@ordRef, @userId, @schemeId, 'redemption', @units, 'initiated');
              SELECT LAST_INSERT_ID();",
                new { ordRef, userId, req.schemeId, units = req.units });

            return Ok(new { orderId, orderRef = ordRef, status = "initiated" });
        }

        // GET: api/<OrdersController>
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET api/<OrdersController>/5
        [HttpGet("{id}")]
        public string Get(int id)
        {
            return "value";
        }

        // POST api/<OrdersController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<OrdersController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<OrdersController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
