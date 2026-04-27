using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Infrastructure.Data;

namespace PaymentGateway.Tests.Integration.Helpers;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string JwtSecretKey = "test-signing-key-for-integration-tests-only!!";
    private const string JwtIssuer = "payment-gateway-api";
    private const string JwtAudience = "payment-gateway-clients";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace SQL Server with InMemory so tests run without a database
            services.RemoveAll<DbContextOptions<PaymentDbContext>>();
            services.AddDbContext<PaymentDbContext>(options =>
                options.UseInMemoryDatabase("TestDb_" + Guid.NewGuid()));

            // Replace Redis-backed services with no-op implementations
            services.RemoveAll<ICacheService>();
            services.RemoveAll<IIdempotencyService>();
            services.AddScoped<ICacheService, NullCacheService>();
            services.AddScoped<IIdempotencyService, NullIdempotencyService>();

            // Override JWT validation parameters so tests control the signing key
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = JwtIssuer,
                    ValidAudience = JwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecretKey)),
                    ClockSkew = TimeSpan.Zero
                };
            });
        });

        builder.UseEnvironment("Testing");
    }

    public string GenerateTestToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: [new Claim(ClaimTypes.Name, "test-client")],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
