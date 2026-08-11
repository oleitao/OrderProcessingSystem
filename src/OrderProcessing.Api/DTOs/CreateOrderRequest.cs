using System.ComponentModel.DataAnnotations;

namespace OrderProcessing.Api.DTOs;

public sealed record CreateOrderItemRequest(
    [Required(AllowEmptyStrings = false)]
    string ProductName,

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
    int Quantity,

    [Range(0.01, (double)decimal.MaxValue, ErrorMessage = "UnitPrice must be greater than zero.")]
    decimal UnitPrice);

public sealed record CreateOrderRequest(
    [Required(AllowEmptyStrings = false)]
    string CustomerName,

    [Required(AllowEmptyStrings = false), EmailAddress]
    string CustomerEmail,

    [Required, MinLength(1, ErrorMessage = "Order must contain at least one item.")]
    IReadOnlyCollection<CreateOrderItemRequest> Items);
