using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Webkassa.IikoFrontAdapter.Spike;

[DataContract]
public sealed class IikoChequeDraft
{
    [DataMember(Name = "isReturn")]
    public bool IsReturn { get; set; }

    [DataMember(Name = "orderId")]
    public string OrderId { get; set; } = string.Empty;

    [DataMember(Name = "orderNumber")]
    public string OrderNumber { get; set; } = string.Empty;

    [DataMember(Name = "paymentId")]
    public string? PaymentId { get; set; }

    [DataMember(Name = "refundId")]
    public string? RefundId { get; set; }

    [DataMember(Name = "cashierName")]
    public string? CashierName { get; set; }

    [DataMember(Name = "cashierId")]
    public Guid? CashierId { get; set; }

    [DataMember(Name = "operationTime")]
    public DateTime OperationTime { get; set; }

    [DataMember(Name = "resultSum")]
    public decimal ResultSum { get; set; }

    [DataMember(Name = "discountSum")]
    public decimal? DiscountSum { get; set; }

    [DataMember(Name = "increaseSum")]
    public decimal? IncreaseSum { get; set; }

    [DataMember(Name = "roundSum")]
    public decimal? RoundSum { get; set; }

    [DataMember(Name = "customer")]
    public IikoCustomerDraft Customer { get; set; } = new IikoCustomerDraft();

    [DataMember(Name = "positions")]
    public List<IikoChequePositionDraft> Positions { get; private set; } = new List<IikoChequePositionDraft>();

    [DataMember(Name = "payments")]
    public List<IikoPaymentDraft> Payments { get; private set; } = new List<IikoPaymentDraft>();

    [DataMember(Name = "warnings")]
    public List<string> Warnings { get; private set; } = new List<string>();
}

[DataContract]
public sealed class IikoChequePositionDraft
{
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "count")]
    public decimal Count { get; set; }

    [DataMember(Name = "price")]
    public decimal Price { get; set; }

    [DataMember(Name = "sum")]
    public decimal Sum { get; set; }

    [DataMember(Name = "code")]
    public string? Code { get; set; }

    [DataMember(Name = "productId")]
    public Guid? ProductId { get; set; }

    [DataMember(Name = "discount")]
    public decimal? Discount { get; set; }

    [DataMember(Name = "markup")]
    public decimal? Markup { get; set; }

    [DataMember(Name = "vat")]
    public decimal? Vat { get; set; }

    [DataMember(Name = "sectionCode")]
    public int? SectionCode { get; set; }

    [DataMember(Name = "nkt")]
    public IikoChequePositionNktDraft Nkt { get; set; } = new IikoChequePositionNktDraft();

    [DataMember(Name = "orderItemIds")]
    public List<Guid> OrderItemIds { get; private set; } = new List<Guid>();
}

[DataContract]
public sealed class IikoChequePositionNktDraft
{
    [DataMember(Name = "gtin")]
    public string? Gtin { get; set; }

    [DataMember(Name = "ntin")]
    public string? Ntin { get; set; }

    [DataMember(Name = "xtin")]
    public string? Xtin { get; set; }

    [DataMember(Name = "nktCode")]
    public string? NktCode { get; set; }

    [DataMember(Name = "productId")]
    public string? ProductId { get; set; }

    [DataMember(Name = "name")]
    public string? Name { get; set; }
}

[DataContract]
public sealed class IikoPaymentDraft
{
    [DataMember(Name = "paymentType")]
    public string PaymentType { get; set; } = string.Empty;

    [DataMember(Name = "sum")]
    public decimal Sum { get; set; }

    [DataMember(Name = "paymentId")]
    public string? PaymentId { get; set; }

    [DataMember(Name = "name")]
    public string? Name { get; set; }
}

[DataContract]
public sealed class IikoCustomerDraft
{
    [DataMember(Name = "email")]
    public string? Email { get; set; }

    [DataMember(Name = "phone")]
    public string? Phone { get; set; }

    [DataMember(Name = "name")]
    public string? Name { get; set; }

    [DataMember(Name = "xin")]
    public string? Xin { get; set; }
}
