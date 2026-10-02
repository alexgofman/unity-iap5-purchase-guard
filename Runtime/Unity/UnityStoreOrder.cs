using System.Collections.Generic;
using UnityEngine.Purchasing;

namespace Iap5PurchaseGuard
{
    /// <summary>
    /// Reads a Unity IAP 5 order (pending, confirmed, deferred or failed: they all derive from
    /// <c>Order</c>) into the SDK-free shape the purchase flow works with.
    /// </summary>
    internal sealed class UnityStoreOrder : IStoreOrder
    {
        public UnityStoreOrder(Order order)
        {
            Native = order;
            TransactionId = string.Empty;
            ProductId = string.Empty;
            StoreProductId = string.Empty;
            StoreName = string.Empty;
            Receipt = string.Empty;

            if (order == null)
            {
                return;
            }

            IOrderInfo info = order.Info;
            if (info != null)
            {
                TransactionId = info.TransactionID ?? string.Empty;
                Receipt = info.Receipt ?? string.Empty;
                StoreName = ReceiptEnvelope.ReadStoreName(Receipt);
            }

            // The Google Play and App Store carts in Unity IAP 5 hold one product. An order with
            // any other number of products is left without a product id, so the flow treats it as
            // unidentified instead of granting part of it. The quantity is not read.
            ICart cart = order.CartOrdered;
            IReadOnlyList<CartItem> items = cart != null ? cart.Items() : null;
            if (items != null && items.Count == 1 && items[0] != null && items[0].Product != null)
            {
                ProductDefinition definition = items[0].Product.definition;
                if (definition != null)
                {
                    ProductId = definition.id ?? string.Empty;
                    StoreProductId = definition.storeSpecificId ?? string.Empty;
                }
            }
        }

        public Order Native { get; }

        public string TransactionId { get; }

        public string ProductId { get; }

        public string StoreProductId { get; }

        public string StoreName { get; }

        public string Receipt { get; }
    }
}
