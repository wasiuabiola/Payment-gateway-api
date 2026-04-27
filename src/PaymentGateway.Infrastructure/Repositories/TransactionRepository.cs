using Microsoft.EntityFrameworkCore;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Models;
using PaymentGateway.Infrastructure.Data;

namespace PaymentGateway.Infrastructure.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private readonly PaymentDbContext _db;

    public TransactionRepository(PaymentDbContext db) => _db = db;

    private IQueryable<Transaction> TransactionsWithEvents =>
        _db.Transactions.Include(t => t.Events);

    public async Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await TransactionsWithEvents.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Transaction?> GetByReferenceAsync(string reference, CancellationToken ct = default)
        => await TransactionsWithEvents.FirstOrDefaultAsync(t => t.Reference == reference, ct);

    public async Task<IEnumerable<Transaction>> GetByCustomerEmailAsync(string email, CancellationToken ct = default)
        => await _db.Transactions.Where(t => t.Customer.Email == email).ToListAsync(ct);

    public async Task CreateAsync(Transaction transaction, CancellationToken ct = default)
    {
        await _db.Transactions.AddAsync(transaction, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Transaction transaction, CancellationToken ct = default)
    {
        _db.Transactions.Update(transaction);
        await _db.SaveChangesAsync(ct);
    }
}
