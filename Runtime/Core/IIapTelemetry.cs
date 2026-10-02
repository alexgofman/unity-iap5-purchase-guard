using System;
using System.Collections.Generic;

namespace Iap5PurchaseGuard
{
    /// <summary>
    /// Where the package reports what happened. Forward these to whatever analytics the game uses.
    ///
    /// Calls are wrapped by the purchase flow: an exception thrown by an implementation is
    /// swallowed and can never change the outcome of a purchase.
    /// </summary>
    public interface IIapTelemetry
    {
        /// <summary>
        /// A purchase was granted and its order sent for confirmation. Raised at most once per
        /// transaction, and never for an order the trust gate refused, so it is the event to count
        /// as revenue. While <see cref="ValidationUnavailable"/> is being raised with the fail-open
        /// policy, it includes orders that were granted without a signature check.
        /// </summary>
        void PurchaseGranted(string productId, string transactionId);

        /// <summary>An order was refused by the trust gate. Nothing was granted or confirmed.</summary>
        void OrderRefused(string productId, VerdictReason reason, OrderPath path);

        /// <summary>
        /// Receipt validation cannot run, so orders are being handled by the
        /// <see cref="UnavailablePolicy"/>. Raised once per session. Treat it as an alarm.
        /// </summary>
        void ValidationUnavailable(VerdictReason reason);

        /// <summary>The game's grant threw. On the purchase path the order was left unconfirmed.</summary>
        void GrantFailed(string productId, OrderPath path, Exception error);

        /// <summary>The store or the package ended a purchase attempt without a pending order.</summary>
        void PurchaseFailed(string productId, PurchaseFailureKind kind, string details);

        /// <summary>Products the store no longer reports as owned were revoked.</summary>
        void EntitlementsRevoked(IReadOnlyList<string> productIds);

        /// <summary>
        /// A purchase fetch returned nothing although products were owned before. Nothing was
        /// revoked. See <see cref="OwnedSetDiff"/>.
        /// </summary>
        void EmptyOwnedFetchIgnored(int previouslyOwnedCount);

        /// <summary>The installer of this copy of the app, reported once at start-up.</summary>
        void InstallSourceDetected(string installerName, bool expected);

        /// <summary>Something went wrong that did not change the outcome of a purchase.</summary>
        void Warning(string message);
    }

    /// <summary>
    /// Telemetry that does nothing. Derive from it to override only the events you care about.
    /// </summary>
    public class NullIapTelemetry : IIapTelemetry
    {
        public virtual void PurchaseGranted(string productId, string transactionId)
        {
        }

        public virtual void OrderRefused(string productId, VerdictReason reason, OrderPath path)
        {
        }

        public virtual void ValidationUnavailable(VerdictReason reason)
        {
        }

        public virtual void GrantFailed(string productId, OrderPath path, Exception error)
        {
        }

        public virtual void PurchaseFailed(string productId, PurchaseFailureKind kind, string details)
        {
        }

        public virtual void EntitlementsRevoked(IReadOnlyList<string> productIds)
        {
        }

        public virtual void EmptyOwnedFetchIgnored(int previouslyOwnedCount)
        {
        }

        public virtual void InstallSourceDetected(string installerName, bool expected)
        {
        }

        public virtual void Warning(string message)
        {
        }
    }
}
