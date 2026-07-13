using System;
using System.Collections.Generic;

namespace Webkassa.IikoFrontAdapter.Spike;

public static class WebkassaPrintRequests
{
    private static readonly object Sync = new object();
    private static readonly HashSet<string> Orders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static bool Toggle(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return false;

        lock (Sync)
        {
            if (Orders.Contains(orderId))
            {
                Orders.Remove(orderId);
                return false;
            }

            Orders.Add(orderId);
            return true;
        }
    }

    public static bool IsEnabled(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return false;

        lock (Sync)
            return Orders.Contains(orderId);
    }

    public static bool Consume(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return false;

        lock (Sync)
        {
            if (!Orders.Contains(orderId))
                return false;

            Orders.Remove(orderId);
            return true;
        }
    }
}
