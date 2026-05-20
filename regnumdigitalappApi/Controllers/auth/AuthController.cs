using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Dapper;
using MySql.Data.MySqlClient;
using Org.BouncyCastle.Crypto.Generators;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using static regnumdigitalappApi.Model.modelObject.RequestDTO;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace regnumdigitalappApi.Controllers.auth
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly MySqlConnection _db;
        private readonly IConfiguration _cfg;

        public AuthController(MySqlConnection db, IConfiguration cfg) { _db = db; _cfg = cfg; }

        // POST /api/auth/login
        // Body: { mobile, password }
        // → { token, user: { id, name, role, pan } }
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            var user = await _db.QueryFirstOrDefaultAsync<UserRow>(
                "SELECT id, first_name, last_name, password_hash, role_id, pan " +
                "FROM users WHERE mobile=@mobile AND is_active=1", new { req.mobile });

            if (user == null || !BCrypt.Net.BCrypt.Verify(req.password, user.PasswordHash))
                return Unauthorized(new { message = "Invalid credentials" });

            var token = GenerateJwt(user.Id, user.RoleId);
            await _db.ExecuteAsync(
                "UPDATE users SET last_login_at=NOW() WHERE id=@id", new { id = user.Id });

            return Ok(new
            {
                token,
                user = new { user.Id, name = $"{user.FirstName} {user.LastName}", user.RoleId, user.Pan }
            });
        }

        // POST /api/auth/send-otp
        [HttpPost("send-otp")]
        public async Task<IActionResult> SendOtp([FromBody] OtpRequest req)
        {
            var otp = new Random().Next(100000, 999999).ToString();
            var expiry = DateTime.UtcNow.AddMinutes(10);
            await _db.ExecuteAsync(
                "INSERT INTO otp_store (mobile, otp_code, purpose, expires_at) " +
                "VALUES (@mobile, @otp, 'login', @expiry)",
                new { req.mobile, otp, expiry });
            // TODO: Send via SMS gateway (e.g. MSG91, Twilio)
            return Ok(new { message = "OTP sent" });
        }

        // POST /api/auth/verify-otp
        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest req)
        {
            var row = await _db.QueryFirstOrDefaultAsync(
                "SELECT id FROM otp_store WHERE mobile=@mobile AND otp_code=@otp " +
                "AND is_used=0 AND expires_at > NOW() ORDER BY id DESC LIMIT 1",
                new { req.mobile, otp = req.otp });
            if (row == null) return BadRequest(new { message = "Invalid or expired OTP" });

            await _db.ExecuteAsync("UPDATE otp_store SET is_used=1 WHERE mobile=@mobile", new { req.mobile });

            var user = await _db.QueryFirstOrDefaultAsync<UserRow>(
                "SELECT id, first_name, last_name, role_id, pan FROM users WHERE mobile=@mobile", new { req.mobile });
            if (user == null) return NotFound(new { message = "User not found" });

            var token = GenerateJwt(user.Id, user.RoleId);
            return Ok(new { token, user = new { user.Id, name = $"{user.FirstName} {user.LastName}", user.RoleId } });
        }

        // POST /api/auth/register
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req)
        {
            var exists = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM users WHERE mobile=@mobile OR email=@email",
                new { req.mobile, req.email });
            if (exists > 0) return Conflict(new { message = "Mobile or email already registered" });

            var hash = BCrypt.Net.BCrypt.HashPassword(req.password);
            var userId = await _db.ExecuteScalarAsync<long>(
                "INSERT INTO users (role_id,first_name,last_name,email,mobile,password_hash,pan) " +
                "VALUES (1,@firstName,@lastName,@email,@mobile,@hash,@pan); SELECT LAST_INSERT_ID();",
                new { req.firstName, req.lastName, req.email, req.mobile, hash, req.pan });

            await _db.ExecuteAsync(
                "INSERT INTO kyc_details (user_id, kyc_status) VALUES (@userId, 'pending')", new { userId });

            return Ok(new { message = "Registered successfully. Please complete KYC.", userId });
        }

        private string GenerateJwt(long userId, int roleId)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddHours(double.Parse(_cfg["Jwt:ExpiryHours"]!));
            var claims = new[] {
            new Claim("sub",    userId.ToString()),
            new Claim("roleId", roleId.ToString()),
        };
            var token = new JwtSecurityToken(
                _cfg["Jwt:Issuer"], _cfg["Jwt:Audience"], claims, expires: expires, signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // GET: api/<AuthController>
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET api/<AuthController>/5
        [HttpGet("{id}")]
        public string Get(int id)
        {
            return "value";
        }

        // POST api/<AuthController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<AuthController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<AuthController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
