namespace Iap5PurchaseGuard
{
    /// <summary>What to do when the trust gate reports <see cref="ReceiptVerdict.Unavailable"/>.</summary>
    public enum UnavailablePolicy
    {
        /// <summary>
        /// Grant anyway and raise a telemetry alarm. A missing or unusable key is the app's own
        /// mistake and should not stop every paying player; the alarm is how the mistake is noticed.
        /// While it lasts, purchases are granted without the signature check.
        /// </summary>
        FailOpen = 0,

        /// <summary>
        /// Refuse. Nothing is granted or confirmed while validation is unavailable, so real
        /// purchases stay pending until a build with a working key ships. Google Play refunds a
        /// purchase that is not acknowledged within three days.
        /// </summary>
        FailClosed = 1
    }

    /// <summary>What the purchase flow does with an order after the trust gate.</summary>
    /// <remarks>Refuse is the zero value, so the default is the safe one.</remarks>
    public enum GateDecision
    {
        /// <summary>Do not grant and do not confirm. Tell the player and report it.</summary>
        Refuse = 0,

        /// <summary>Grant the entitlement, then confirm the order.</summary>
        Grant = 1,

        /// <summary>Do not grant and do not confirm yet. The store delivers the order again.</summary>
        Hold = 2
    }

    /// <summary>
    /// Maps a verdict to a decision. This is the whole fail-open versus fail-closed policy:
    ///
    /// <code>
    /// verdict       decision
    /// Valid         Grant
    /// Invalid       Refuse   (fail closed: a doubtful receipt is never a free grant)
    /// Deferred      Hold
    /// Unavailable   Grant with FailOpen, Refuse with FailClosed
    /// </code>
    ///
    /// The asymmetry is intentional. Invalid is a statement about the receipt, which the other
    /// side controls, so it always fails closed. Unavailable is a statement about the app's own
    /// configuration, so the app owner chooses.
    /// </summary>
    public sealed class VerdictPolicy
    {
        public VerdictPolicy(UnavailablePolicy whenUnavailable)
        {
            WhenUnavailable = whenUnavailable;
        }

        public UnavailablePolicy WhenUnavailable { get; }

        public GateDecision Decide(ReceiptVerdict verdict)
        {
            switch (verdict)
            {
                case ReceiptVerdict.Valid:
                    return GateDecision.Grant;
                case ReceiptVerdict.Deferred:
                    return GateDecision.Hold;
                case ReceiptVerdict.Unavailable:
                    return WhenUnavailable == UnavailablePolicy.FailOpen ? GateDecision.Grant : GateDecision.Refuse;
                default:
                    // Invalid, and any value this version does not know about.
                    return GateDecision.Refuse;
            }
        }
    }
}
