using FluentAssertions;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Models;
using Xunit;

namespace PaymentGateway.Tests.Domain;

public class TransactionTests
{
    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_InitializesWithPendingStatusAndNoEvents()
    {
        var transaction = BuildTransaction("TXN-001");

        transaction.Status.Should().Be(PaymentStatus.Pending);
        transaction.Reference.Should().Be("TXN-001");
        transaction.AmountInKobo.Should().Be(50000);
        transaction.Currency.Should().Be(Currency.NGN);
        transaction.Provider.Should().Be(ProviderType.Paystack);
        transaction.Events.Should().BeEmpty();
        transaction.ProviderReference.Should().BeNull();
        transaction.AuthorizationUrl.Should().BeNull();
        transaction.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Create_AssignsUniqueIdOnEachCall()
    {
        var t1 = BuildTransaction("TXN-001");
        var t2 = BuildTransaction("TXN-002");

        t1.Id.Should().NotBe(t2.Id);
    }

    [Fact]
    public void Create_WithNullMetadata_InitializesEmptyDictionary()
    {
        var transaction = Transaction.Create(
            "TXN-001", 50000, Currency.NGN, ProviderType.Paystack,
            new CustomerInfo { Email = "a@b.com", Name = "Test" },
            metadata: null);

        transaction.Metadata.Should().NotBeNull().And.BeEmpty();
    }

    // ── BeginProcessing ──────────────────────────────────────────────────────

    [Fact]
    public void BeginProcessing_TransitionsToProcessingAndRecordsEvent()
    {
        var transaction = BuildTransaction("TXN-001");

        transaction.BeginProcessing("prov_ref_001", "https://checkout.paystack.com/abc");

        transaction.Status.Should().Be(PaymentStatus.Processing);
        transaction.ProviderReference.Should().Be("prov_ref_001");
        transaction.AuthorizationUrl.Should().Be("https://checkout.paystack.com/abc");
        transaction.Events.Should().ContainSingle(e => e.EventType == nameof(transaction.BeginProcessing));
    }

    // ── Complete ─────────────────────────────────────────────────────────────

    [Fact]
    public void Complete_TransitionsToSuccessfulAndRecordsEvent()
    {
        var transaction = BuildTransaction("TXN-001");
        transaction.BeginProcessing("prov_ref_001", "https://checkout.paystack.com/abc");

        transaction.Complete("prov_ref_001");

        transaction.Status.Should().Be(PaymentStatus.Successful);
        transaction.ProviderReference.Should().Be("prov_ref_001");
        transaction.Events.Should().HaveCount(2);
        transaction.Events.Last().EventType.Should().Be(nameof(transaction.Complete));
    }

    // ── Fail ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Fail_TransitionsToFailedAndPersistsReason()
    {
        var transaction = BuildTransaction("TXN-001");

        transaction.Fail("Insufficient funds");

        transaction.Status.Should().Be(PaymentStatus.Failed);
        transaction.FailureReason.Should().Be("Insufficient funds");
        transaction.Events.Should().ContainSingle(e =>
            e.EventType == nameof(transaction.Fail) && e.Description == "Insufficient funds");
    }

    // ── Refund ───────────────────────────────────────────────────────────────

    [Fact]
    public void Refund_TransitionsToRefundedAndRecordsEvent()
    {
        var transaction = BuildTransaction("TXN-001");
        transaction.BeginProcessing("prov_ref_001", "https://checkout.paystack.com/abc");
        transaction.Complete("prov_ref_001");

        transaction.Refund();

        transaction.Status.Should().Be(PaymentStatus.Refunded);
        transaction.Events.Should().HaveCount(3);
        transaction.Events.Last().EventType.Should().Be(nameof(transaction.Refund));
    }

    // ── Events ───────────────────────────────────────────────────────────────

    [Fact]
    public void Events_AreLinkedToTransactionId()
    {
        var transaction = BuildTransaction("TXN-001");
        transaction.BeginProcessing("ref", "url");

        transaction.Events.Should().AllSatisfy(e => e.TransactionId.Should().Be(transaction.Id));
    }

    [Fact]
    public void Events_HaveUniqueIds()
    {
        var transaction = BuildTransaction("TXN-001");
        transaction.BeginProcessing("ref", "url");
        transaction.Complete("ref");
        transaction.Refund();

        var eventIds = transaction.Events.Select(e => e.Id).ToList();
        eventIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Events_OccurredAtIsSetToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var transaction = BuildTransaction("TXN-001");
        transaction.BeginProcessing("ref", "url");
        var after = DateTime.UtcNow.AddSeconds(1);

        transaction.Events.Should().AllSatisfy(e =>
            e.OccurredAt.Should().BeAfter(before).And.BeBefore(after));
    }

    // ── Customer ─────────────────────────────────────────────────────────────

    [Fact]
    public void Create_StoresCustomerInfo()
    {
        var customer = new CustomerInfo { Email = "customer@example.com", Name = "Jane Doe", Phone = "+2348012345678" };
        var transaction = Transaction.Create("TXN-001", 10000, Currency.NGN, ProviderType.Paystack, customer);

        transaction.Customer.Email.Should().Be("customer@example.com");
        transaction.Customer.Name.Should().Be("Jane Doe");
        transaction.Customer.Phone.Should().Be("+2348012345678");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Transaction BuildTransaction(string reference) =>
        Transaction.Create(reference, 50000, Currency.NGN, ProviderType.Paystack,
            new CustomerInfo { Email = "test@example.com", Name = "Test User" });
}
