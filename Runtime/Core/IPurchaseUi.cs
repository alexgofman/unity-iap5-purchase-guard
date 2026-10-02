namespace Iap5PurchaseGuard
{
    /// <summary>Why a purchase ended without a grant, in terms a player-facing message can use.</summary>
    public enum PurchaseFailureKind
    {
        Other = 0,
        Cancelled,
        AlreadyOwned,
        PaymentDeclined,
        ProductUnavailable,
        StoreNotReady,
        UnofficialInstall,
        GrantFailed
    }

    /// <summary>
    /// Player-facing feedback. Every purchase the pipeline starts ends in at least one of these
    /// calls, so a paywall can use the first one to release its busy state. Two of them can be
    /// followed by a second call later: a pending payment by its eventual result, and an "already
    /// owned" failure by the result of the order the store then hands over. An order that is
    /// delivered again after it was already granted is confirmed without any call, unless it is
    /// the store's answer to a purchase the player just started, which then ends as already owned.
    ///
    /// The calls are made once the order is safe (granted, remembered and sent for confirmation,
    /// or left unconfirmed), and an exception thrown here is caught and reported: UI code can
    /// never change the outcome of a purchase.
    /// </summary>
    public interface IPurchaseUi
    {
        /// <summary>
        /// The product was granted and the order sent for confirmation. This can also arrive at
        /// launch, when an order that could not be completed earlier is delivered again.
        /// </summary>
        void PurchaseSucceeded(string productId);

        /// <summary>
        /// The order could not be verified, so nothing was unlocked. The product id is empty when
        /// the order did not name a known product.
        /// </summary>
        void PurchaseRefused(string productId, VerdictReason reason);

        /// <summary>
        /// The store is still processing the payment. The product unlocks when the payment
        /// completes; the player should not buy it again. This can also arrive at launch for an
        /// order that is still waiting.
        /// </summary>
        void PurchasePending(string productId);

        /// <summary>The purchase did not go through.</summary>
        void PurchaseFailed(string productId, PurchaseFailureKind kind);
    }

    /// <summary>A UI that shows nothing. Used when no UI is supplied.</summary>
    public sealed class NullPurchaseUi : IPurchaseUi
    {
        public static readonly NullPurchaseUi Instance = new NullPurchaseUi();

        public void PurchaseSucceeded(string productId)
        {
        }

        public void PurchaseRefused(string productId, VerdictReason reason)
        {
        }

        public void PurchasePending(string productId)
        {
        }

        public void PurchaseFailed(string productId, PurchaseFailureKind kind)
        {
        }
    }
}
