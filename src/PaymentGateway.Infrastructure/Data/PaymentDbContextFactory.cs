using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace PaymentGateway.Infrastructure.Data;

public class PaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        // PMC runs from the startup project directory (PaymentGateway.API)
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? "Server=localhost;Database=PaymentGateway;Trusted_Connection=True;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new PaymentDbContext(options);
    }
}
