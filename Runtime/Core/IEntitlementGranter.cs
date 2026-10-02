namespace Iap5PurchaseGuard
{
    /// <summary>Why an entitlement is being granted.</summary>
    public enum GrantSource
    {
        /// <summary>A purchase the player just paid for, or its redelivery after a failed grant.</summary>
        Purchase = 0,

        /// <summary>The store reports the product as owned and it is not active on this device.</summary>
        Restore = 1
    }

    /// <summary>
    /// The game's side of a purchase: applying, checking and removing entitlements. The package
    /// decides when these are called; it knows nothing about what a product unlocks.
    /// </summary>
    public interface IEntitlementGranter
    {
        /// <summary>
        /// True when the durable entitlement for this product is already active on this device.
        /// Used on the restore path, so an owned product is not granted again on every launch.
        /// </summary>
        bool IsEntitlementActive(string productId);

        /// <summary>
        /// Applies the entitlement. An exception leaves the order unconfirmed, so the store
        /// delivers it again and the grant is retried; make the grant atomic or safe to repeat.
        /// For <see cref="GrantSource.Restore"/>, unlock the product but do not hand out any
        /// one-time contents again.
        ///
        /// The grant and the ledger write that follows it are two steps. If the app is killed
        /// between them, the order is delivered again and this method is called a second time for
        /// the same <paramref name="transactionId"/>. A game that cannot tolerate that should
        /// record the transaction id in the same save operation as the grant and ignore one it
        /// has seen. The id is empty when the store supplied none, which only happens on the
        /// restore path.
        /// </summary>
        void Grant(string productId, string transactionId, GrantSource source);

        /// <summary>Removes an entitlement the store no longer reports as owned.</summary>
        void Revoke(string productId);
    }
}
