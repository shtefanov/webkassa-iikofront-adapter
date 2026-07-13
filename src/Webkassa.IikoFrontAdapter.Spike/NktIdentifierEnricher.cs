using System;

namespace Webkassa.IikoFrontAdapter.Spike;

public static class NktIdentifierEnricher
{
    public static int Enrich(IikoChequeDraft draft)
    {
        if (draft == null)
            throw new ArgumentNullException(nameof(draft));

        var enriched = 0;
        foreach (var position in draft.Positions)
        {
            var productId = position.ProductId.HasValue ? position.ProductId.Value.ToString("D") : string.Empty;
            if (!NationalCatalogSyncQueue.TryFindIdentifier(productId, position.Code, out var identifier))
                continue;

            position.Nkt.Ntin = FirstNonEmpty(position.Nkt.Ntin, identifier.Ntin);
            position.Nkt.Gtin = FirstNonEmpty(position.Nkt.Gtin, identifier.Gtin);
            position.Nkt.Xtin = FirstNonEmpty(position.Nkt.Xtin, identifier.Xtin);
            position.Nkt.ProductId = FirstNonEmpty(position.Nkt.ProductId, identifier.ProductId);
            position.Nkt.Name = FirstNonEmpty(position.Nkt.Name, identifier.Name);
            enriched++;
        }

        return enriched;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
