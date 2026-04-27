using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.API.DTOs.Requests;

public class CreatePaymentRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Reference { get; init; } = default!;

    [Required]
    [Range(1, long.MaxValue, ErrorMessage = "AmountInKobo must be greater than 0")]
    public long AmountInKobo { get; init; }

    [Required]
    [RegularExpression("^(NGN|GBP|USD|EUR)$", ErrorMessage = "Currency must be NGN, GBP, USD, or EUR")]
    public string Currency { get; init; } = default!;

    [Required]
    [RegularExpression("^(Paystack|Interswitch|CyberSource)$", ErrorMessage = "Provider must be Paystack, Interswitch, or CyberSource")]
    public string Provider { get; init; } = default!;

    [Required]
    public CustomerDto Customer { get; init; } = default!;

    public Dictionary<string, string>? Metadata { get; init; }
}

public class CustomerDto
{
    [Required]
    [EmailAddress]
    [StringLength(200)]
    public string Email { get; init; } = default!;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = default!;

    [Phone]
    [StringLength(50)]
    public string? Phone { get; init; }
}

public class RefundRequest
{
    [Required]
    [Range(1, long.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public long Amount { get; init; }

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public string Reason { get; init; } = default!;
}
