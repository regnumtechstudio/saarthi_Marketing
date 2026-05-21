using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using modelObject;
using MySql.Data.MySqlClient;
using RegnumDigital.API.Data;
using RegnumDigital.API.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var connStr = config.GetConnectionString("RegnumDB");

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseMySql(connStr, ServerVersion.AutoDetect(connStr)));


//builder.Services.AddDbContextFactory<AppDbContext>(options =>
//{
//    options.UseSqlServer(connStr);
//});

//builder.Services.AddDbContextFactory<AppDbContext>(opt =>
//    opt.UseMySql(connStr, ServerVersion.AutoDetect(connStr)));

//builder.Services.Configure<IdentityOptions>(options =>
//    options.ClaimsIdentity.UserIdClaimType = ClaimTypes.NameIdentifier);

//JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

builder.Services.AddHttpContextAccessor();

// ── CORS — allow all origins for local dev ────────────────────
//builder.Services.AddCors(opt =>
//{
//    opt.AddPolicy("AllowAll", p =>
//        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
//});

// ── Authorization Policies ────────────────────────────────────
builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("AdminOnly", p => p.RequireClaim("userType", "admin"));
    opt.AddPolicy("PartnerOnly", p => p.RequireClaim("userType", "partner"));
  
});

// MySQL
builder.Services.AddTransient<MySqlConnection>(_ => new MySqlConnection(connStr));

// CORS — allow your HTML pages to call this API
builder.Services.AddCors(opt => opt.AddPolicy("RegnumPolicy", b =>
//b.WithOrigins(config["Cors:AllowedOrigins"]!.Split(','))
// .AllowAnyHeader()
// .AllowAnyMethod()));

 b.WithOrigins(config.GetSection("Cors:AllowedOrigins").Get<List<string>>().ToArray())
 .AllowAnyHeader()
 .AllowAnyMethod()));

   builder.Services.AddSwaggerGen();

// JWT Auth
var jwtKey = config["Jwt:Key"]!;


//builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
//    .AddCookie();
// Add services to the container.

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddAuthentication(opt =>
{
    opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
   
}).AddJwtBearer(opt =>
{
    opt.IncludeErrorDetails = true;
    opt.TokenValidationParameters = new TokenValidationParameters()
    {
       
        ValidateIssuerSigningKey = true,
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        ValidIssuer = config["Jwt:Issuer"],
        ValidAudience = config["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
}).AddCookie();


// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle

//builder.Services.AddAuthorization();
//builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPromoEngine, PromoEngine>();
//builder.Services.AddScoped<ICheckoutService, CheckoutService>();
//builder.Services.AddScoped<IPaymentService, RazorpayPaymentService>();
//builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
//builder.Services.AddScoped<IEntitlementService, EntitlementService>();
//builder.Services.AddScoped<IAuditService, AuditService>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{ 
    app.UseSwagger();
    app.UseSwaggerUI();
    //app.UseSwaggerUI(c =>
    //{
    //    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Regnum Digital API v1");
    //    c.RoutePrefix = "swagger";
    //});
   // app.UseRouting();
   // app.UseCors("AllowAll");   // too late for images

    // ✅ AFTER — CORS runs first, then static files get the headers   
    //app.UseStaticFiles(new StaticFileOptions
    //{
    //    OnPrepareResponse = ctx => {
    //        ctx.Context.Response.Headers["Access-Control-Allow-Origin"] = "*";
    //        ctx.Context.Response.Headers["Cache-Control"] = "no-store"; // prevents replaying old cached response
    //    }
    //});
   // app.use(express.static ('public'));
}

DefaultFilesOptions options = new DefaultFilesOptions();
options.DefaultFileNames.Clear(); // Clear default names
options.DefaultFileNames.Add("index.html"); // Add your custom page name
app.UseDefaultFiles(options);
app.UseStaticFiles();
   
// serves /wwwroot/uploads/
//app.UseCors();
app.UseCors(options =>
                        options.WithOrigins(
                                            "http://localhost:4200",
                                            "http://localhost:4200/",
                                            "https://localhost:4200",
                                            "https://localhost:4200/",
                                             "http://localhost:7101",
                                            "http://localhost:7101/",
                                            "https://localhost:7101",
                                            "https://localhost:7101/",
                                            "http://localhost:4300",
                                            "http://localhost:4300/",
                                            "https://localhost:4300",
                                            "https://localhost:4300/",
                                            "http://192.168.0.97:5500",
                                            "http://192.168.0.97:5500/",
                                            "https://192.168.0.97:5500",
                                            "http://192.168.0.97:5500/",
                                            "http://127.0.0.1:5500",
                                            "http://127.0.0.1:5500/",
                                            "http://192.168.0.96:5500/",
                                            "http://192.168.0.96:5500",
                                            "http://192.168.0.96:231/",
                                            "http://192.168.0.96:231",
                                            "http://192.168.0.96:4200",
                                            "http://192.168.0.96:4200/",
                                            "http://saarthi.regnumdigital.co.in",                                            
                                            "http://saarthi.regnumdigital.co.in/",
                                            "https://saarthi.regnumdigital.co.in",
                                            "https://saarthi.regnumdigital.co.in/"
                                        )
                                       .AllowAnyHeader()
                                       .AllowCredentials()
                                       .AllowAnyMethod());
app.UseRouting();
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});
app.Run();
