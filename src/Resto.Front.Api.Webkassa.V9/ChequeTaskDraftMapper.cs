using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Resto.Front.Api.Data.Device.Tasks;

namespace Resto.Front.Api.Webkassa.V9;

public static class ChequeTaskDraftMapper
{
    public static IikoChequeDraft Map(ChequeTask task)
    {
        if (task == null)
            throw new ArgumentNullException(nameof(task));

        var draft = new IikoChequeDraft
        {
            IsReturn = task.IsRefund || task.IsProductRefund || task.IsCancellation,
            OrderId = task.OrderId.ToString("D"),
            OrderNumber = task.OrderNumber.ToString(CultureInfo.InvariantCulture),
            PaymentId = BuildPaymentId(task),
            RefundId = BuildRefundId(task),
            CashierName = NullIfWhiteSpace(task.CashierName),
            CashierId = task.CashierId == Guid.Empty ? null : task.CashierId,
            OperationTime = task.OperationTime,
            ResultSum = task.ResultSum,
            DiscountSum = task.DiscountSum,
            IncreaseSum = task.IncreaseSum,
            RoundSum = task.RoundSum,
            Customer =
            {
                Email = NullIfWhiteSpace(task.OfdEmail),
                Phone = NullIfWhiteSpace(task.OfdPhoneNumber),
                Name = NullIfWhiteSpace(task.CustomerDetailsInfo?.Name),
                Xin = NullIfWhiteSpace(task.CustomerDetailsInfo?.Code)
            }
        };

        foreach (var sale in task.Sales)
            draft.Positions.Add(MapSale(sale));

        AddPayments(draft, task);
        AddValidationWarnings(draft);

        return draft;
    }

    public static string BuildExternalCheckNumber(IikoChequeDraft draft)
    {
        if (draft == null)
            throw new ArgumentNullException(nameof(draft));

        var prefix = draft.IsReturn ? "iiko-return" : "iiko-sale";
        var tail = draft.IsReturn
            ? FirstNonEmpty(draft.RefundId, draft.PaymentId, draft.OrderNumber)
            : FirstNonEmpty(draft.PaymentId, draft.OrderNumber);

        var readable = $"{prefix}-{draft.OrderId}-{tail}";
        if (readable.Length <= 50)
            return readable;

        using (var sha256 = SHA256.Create())
        {
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(readable));
            var digest = string.Concat(hash.Take(8).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            return $"{prefix}-{digest}";
        }
    }

    private static IikoChequePositionDraft MapSale(ChequeSale sale)
    {
        var count = sale.Amount ?? 1m;
        var sum = sale.Sum ?? RoundMoney((sale.Price ?? 0m) * count);
        var price = sale.Price ?? (count == 0m ? sum : RoundMoney(sum / count));

        var draft = new IikoChequePositionDraft
        {
            Name = sale.Name,
            Count = count,
            Price = price,
            Sum = sum,
            Code = NullIfWhiteSpace(sale.Code),
            ProductId = sale.ProductId == Guid.Empty ? null : sale.ProductId,
            Discount = sale.DiscountSum,
            Markup = sale.IncreaseSum,
            IsTaxable = sale.IsTaxable,
            TaxPercent = sale.IsTaxable ? sale.Vat : null,
            SectionCode = sale.Section
        };

        draft.Nkt.Gtin = NullIfWhiteSpace(sale.GtinCode);

        if (sale.Codes != null)
        {
            draft.MarkList.AddRange(sale.Codes
                .Where(group => group != null)
                .SelectMany(group => group)
                .Where(identifier => identifier != null && !string.IsNullOrWhiteSpace(identifier.Code))
                .Select(identifier => identifier.Code));
        }

        if (sale.OrderItemIds != null)
            draft.OrderItemIds.AddRange(sale.OrderItemIds.Where(id => id != Guid.Empty));

        return draft;
    }

    private static void AddPayments(IikoChequeDraft draft, ChequeTask task)
    {
        if (task.CashPayments != null && task.CashPayments.Count > 0)
        {
            foreach (var payment in task.CashPayments)
                AddChequePayment(draft, "cash", payment);
        }
        else if (task.CashPayment > 0m)
        {
            draft.Payments.Add(new IikoPaymentDraft
            {
                PaymentType = "cash",
                Sum = task.CashPayment
            });
        }

        if (task.CardPayments != null)
        {
            foreach (var payment in task.CardPayments)
                AddChequePayment(draft, "card", payment);
        }

        if (task.PrepaymentSum > 0m)
        {
            draft.Payments.Add(new IikoPaymentDraft
            {
                PaymentType = "prepayment",
                Sum = task.PrepaymentSum,
                PaymentId = task.PrepaymentIds == null ? null : string.Join(",", task.PrepaymentIds)
            });
        }

        if (task.CreditSum > 0m)
        {
            draft.Payments.Add(new IikoPaymentDraft
            {
                PaymentType = "credit",
                Sum = task.CreditSum
            });
        }

        if (task.ConsiderationSum > 0m)
        {
            draft.Payments.Add(new IikoPaymentDraft
            {
                PaymentType = "consideration",
                Sum = task.ConsiderationSum
            });
        }
    }

    private static void AddChequePayment(IikoChequeDraft draft, string fallbackType, ChequePayment payment)
    {
        draft.Payments.Add(new IikoPaymentDraft
        {
            PaymentType = fallbackType,
            Sum = payment.Sum,
            PaymentId = NullIfWhiteSpace(payment.PaymentRegisterId),
            Name = NullIfWhiteSpace(payment.Name)
        });
    }

    private static void AddValidationWarnings(IikoChequeDraft draft)
    {
        if (draft.Positions.Count == 0)
            draft.Warnings.Add("ChequeTask has no sale lines.");

        if (draft.Payments.Count == 0 && draft.ResultSum != 0m)
            draft.Warnings.Add("ChequeTask has non-zero result sum but no payments.");

        var positionTotal = draft.Positions.Sum(position => position.Sum);
        var paymentTotal = draft.Payments.Sum(payment => payment.Sum);

        if (Math.Abs(positionTotal - draft.ResultSum) > 0.01m)
            draft.Warnings.Add($"Position total {positionTotal} differs from result sum {draft.ResultSum}.");

        if (draft.Payments.Count > 0 && Math.Abs(paymentTotal - draft.ResultSum) > 0.01m)
            draft.Warnings.Add($"Payment total {paymentTotal} differs from result sum {draft.ResultSum}.");
    }

    private static string BuildPaymentId(ChequeTask task)
    {
        if (task.Id.HasValue)
            return task.Id.Value.ToString("D");

        if (task.BillNumber > 0)
            return $"bill-{task.BillNumber.ToString(CultureInfo.InvariantCulture)}";

        return $"order-{task.OrderNumber.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string? BuildRefundId(ChequeTask task)
    {
        if (!(task.IsRefund || task.IsProductRefund || task.IsCancellation))
            return null;

        // iikoFront can create a new ChequeTask.Id when a storno is retried
        // after restart. CancellingSaleNumber identifies the original fiscal
        // sale and stays stable across those retries, so it must win over the
        // transient task id for Webkassa idempotency.
        if (task.CancellingSaleNumber > 0)
            return $"cancel-sale-{task.CancellingSaleNumber.ToString(CultureInfo.InvariantCulture)}";

        if (task.Id.HasValue)
            return task.Id.Value.ToString("D");

        return $"refund-order-{task.OrderNumber.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value!;
        }

        return "unknown";
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static decimal RoundMoney(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
