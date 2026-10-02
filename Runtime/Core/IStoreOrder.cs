namespace Iap5PurchaseGuard
{
    /// <summary>
    /// The facts about one store order that the purchase flow needs, with no SDK types, so the flow
    /// can be driven by a fake store in tests. Every member returns an empty string, never null,
    /// when the store did not supply the value.
    /// </summary>
    public interface IStoreOrder
    {
        /// <summary>The store's transaction id: stable across sessions and redeliveries.</summary>
        string TransactionId { get; }

        /// <summary>
        /// Catalogue id of the product in the order. Empty when the order does not name exactly one
        /// product that can be resolved.
        /// </summary>
        string ProductId { get; }

        /// <summary>The store's own id for that product, which is the id a store receipt carries.</summary>
        string StoreProductId { get; }

        /// <summary>Name of the store that issued the receipt, as stamped on the receipt.</summary>
        string StoreName { get; }

        /// <summary>The raw receipt.</summary>
        string Receipt { get; }
    }

    /// <summary>Which way an order reached the flow.</summary>
    public enum OrderPath
    {
        /// <summary>A pending order: a new purchase, or the redelivery of an unconfirmed one.</summary>
        Purchase = 0,

        /// <summary>An order the store reports as already owned (launch sync or restore).</summary>
        Restore = 1
    }
}
