using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PaymentGateway.API.Middleware;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Infrastructure.Caching;
using PaymentGateway.Infrastructure.Data;
using PaymentGateway.Infrastructure.Idempotency;
using PaymentGateway.Infrastructure.Providers;
using PaymentGateway.Infrastructure.Repositories;
using PaymentGateway.Infrastructure.Services;
using Serilog;
using StackExchange.Redis;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger with JWT support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Payment Gateway API",
        Version = "v1",
        Description = "Multi-provider payment gateway supporting Paystack and Interswitch"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token (obtain one from POST /api/v1/auth/token)"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:SecretKey"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// Rate limiting — fixed window: 100 requests/minute per client IP
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("default", limiter =>
    {
        limiter.PermitLimit = 100;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 10;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Database
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Redis
var redisConnection = builder.Configuration.GetConnectionString("Redis")!;
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

// Caching & Idempotency
builder.Services.AddScoped<ICacheService, RedisCacheService>();
builder.Services.AddScoped<IIdempotencyService, RedisIdempotencyService>();

// Payment Providers — with automatic retry + circuit breaker via Polly
builder.Services.Configure<PaystackOptions>(
    builder.Configuration.GetSection("Providers:Paystack"));
builder.Services.Configure<InterswitchOptions>(
    builder.Configuration.GetSection("Providers:Interswitch"));

builder.Services.AddHttpClient<PaystackProvider>()
    .AddStandardResilienceHandler();

builder.Services.AddHttpClient<InterswitchProvider>()
    .AddStandardResilienceHandler();

builder.Services.AddTransient<IPaymentProvider>(sp => sp.GetRequiredService<PaystackProvider>());
builder.Services.AddTransient<IPaymentProvider>(sp => sp.GetRequiredService<InterswitchProvider>());

// Repositories & Services
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IWebhookService, WebhookService>();

// Health checks
builder.Services.AddHealthChecks()
    .AddRedis(redisConnection, name: "redis")
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "database");

var app = builder.Build();

// Apply pending EF Core migrations on startup so docker compose up works out of the box
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    if (db.Database.IsRelational())
        await db.Database.MigrateAsync();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Payment Gateway API v1"));
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers().RequireRateLimiting("default");
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
