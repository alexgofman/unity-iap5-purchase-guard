namespace Iap5PurchaseGuard
{
    /// <summary>
    /// What the trust gate concluded about one store order. The purchase flow never inspects
    /// receipt details itself; it acts on one of these four values through <see cref="VerdictPolicy"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Invalid"/> is deliberately the zero value, so an uninitialised
    /// <see cref="ReceiptCheck"/> refuses instead of granting.
    /// </remarks>
    public enum ReceiptVerdict
    {
        /// <summary>
        /// Forged, tampered with, issued for another app or product, or missing something every
        /// real order has. Never granted and never confirmed.
        /// </summary>
        Invalid = 0,

        /// <summary>The order is backed by the store for this app and this product, and it is paid.</summary>
        Valid = 1,

        /// <summary>
        /// A genuine order that is not in the purchased state: the payment is still pending, or the
        /// order was cancelled or refunded. Not granted and not confirmed. The store delivers it
        /// again if it becomes paid.
        /// </summary>
        Deferred = 2,

        /// <summary>
        /// The check could not run because of the app's own configuration (no key, unusable key).
        /// Only local configuration may produce this value, never the content of a receipt:
        /// receipt content is attacker-controlled, and this is the one verdict that can fail open.
        /// </summary>
        Unavailable = 3
    }

    /// <summary>
    /// Why a verdict was reached. For telemetry and for choosing a message; decisions are made on
    /// <see cref="ReceiptVerdict"/> alone.
    /// </summary>
    public enum VerdictReason
    {
        None = 0,

        // Reasons for Valid.
        SignatureVerified,
        VerifiedByPlatform,
        NotEnforcedOnThisPlatform,

        // Reasons for Invalid.
        ReceiptMissing,
        TransactionIdMissing,
        ProductUnknown,
        WrongStore,
        TestStoreReceipt,
        ReceiptUnreadable,
        SignatureMismatch,
        WrongApplication,
        NoStoreReceipt,
        WrongProduct,
        WrongTransaction,
        ValidatorRejected,
        GateFailed,

        // Reason for Deferred.
        NotPurchased,

        // Reasons for Unavailable.
        KeyNotConfigured,
        KeyUnusable
    }

    /// <summary>A verdict together with the reason for it.</summary>
    public readonly struct ReceiptCheck
    {
        public ReceiptCheck(ReceiptVerdict verdict, VerdictReason reason)
        {
            Verdict = verdict;
            Reason = reason;
        }

        public ReceiptVerdict Verdict { get; }

        public VerdictReason Reason { get; }

        public static ReceiptCheck Valid(VerdictReason reason)
        {
            return new ReceiptCheck(ReceiptVerdict.Valid, reason);
        }

        public static ReceiptCheck Invalid(VerdictReason reason)
        {
            return new ReceiptCheck(ReceiptVerdict.Invalid, reason);
        }

        public static ReceiptCheck Deferred(VerdictReason reason)
        {
            return new ReceiptCheck(ReceiptVerdict.Deferred, reason);
        }

        public static ReceiptCheck Unavailable(VerdictReason reason)
        {
            return new ReceiptCheck(ReceiptVerdict.Unavailable, reason);
        }

        public override string ToString()
        {
            return Verdict + " (" + Reason + ")";
        }
    }
}
