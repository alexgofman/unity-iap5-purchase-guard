namespace Iap5PurchaseGuard
{
    /// <summary>
    /// The trust gate: decides whether an order is backed by the store. Implementations are
    /// platform-specific; the purchase flow only sees the verdict.
    /// </summary>
    public interface IReceiptGate
    {
        /// <summary>
        /// Judges one order. An implementation that throws is treated as a refusal by the flow,
        /// never as a pass.
        /// </summary>
        ReceiptCheck Check(IStoreOrder order);
    }

    /// <summary>Tells the store that an order has been fulfilled, which ends its redelivery.</summary>
    public interface IOrderConfirmer
    {
        void Confirm(IStoreOrder order);
    }
}
