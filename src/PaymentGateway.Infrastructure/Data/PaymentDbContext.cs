using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PaymentGateway.Core.Models;

namespace PaymentGateway.Infrastructure.Data;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionEvent> TransactionEvents => Set<TransactionEvent>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.ToTable("Transactions");

            entity.Property(t => t.Reference).IsRequired().HasMaxLength(100);
            entity.HasIndex(t => t.Reference).IsUnique();
            entity.Property(t => t.AmountInKobo).IsRequired();
            entity.Property(t => t.Currency).HasConversion<string>().HasMaxLength(10);
            entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(t => t.Provider).HasConversion<string>().HasMaxLength(30);
            entity.Property(t => t.AuthorizationUrl).HasMaxLength(500);
            entity.Property(t => t.ProviderReference).HasMaxLength(200);
            entity.Property(t => t.FailureReason).HasMaxLength(500);

            entity.OwnsOne(t => t.Customer, c =>
            {
                c.Property(ci => ci.Email).HasColumnName("CustomerEmail").IsRequired().HasMaxLength(200);
                c.Property(ci => ci.Name).HasColumnName("CustomerName").IsRequired().HasMaxLength(200);
                c.Property(ci => ci.Phone).HasColumnName("CustomerPhone").HasMaxLength(50);
            });

            entity.Property(t => t.Metadata)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new())
                .HasColumnType("nvarchar(max)");

            entity.HasMany(t => t.Events)
                  .WithOne()
                  .HasForeignKey(e => e.TransactionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TransactionEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("TransactionEvents");
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<WebhookDelivery>(entity =>
        {
            entity.HasKey(w => w.Id);
            entity.ToTable("WebhookDeliveries");
            entity.Property(w => w.Payload).IsRequired().HasColumnType("nvarchar(max)");
            entity.Property(w => w.Status).HasConversion<string>().HasMaxLength(30);
        });
    }
}
