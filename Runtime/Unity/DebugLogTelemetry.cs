using System;
using System.Collections.Generic;
using UnityEngine;

namespace Iap5PurchaseGuard
{
    /// <summary>
    /// Telemetry that writes to the Unity console. It is the default when no telemetry is
    /// supplied; derive from it, or implement <see cref="IIapTelemetry"/>, to forward the events
    /// to an analytics service. Transaction ids are not logged.
    /// </summary>
    public class DebugLogTelemetry : IIapTelemetry
    {
        private const string Tag = "[PurchaseGuard] ";

        public virtual void PurchaseGranted(string productId, string transactionId)
        {
            Debug.Log(Tag + "Granted, confirmation requested: " + productId);
        }

        public virtual void OrderRefused(string productId, VerdictReason reason, OrderPath path)
        {
            Debug.LogWarning(Tag + "Order refused (" + path + "): " + Name(productId) + ", " + reason);
        }

        public virtual void ValidationUnavailable(VerdictReason reason)
        {
            Debug.LogError(Tag + "Receipt validation is unavailable (" + reason + "). Orders are handled by the unavailable policy until this is fixed.");
        }

        public virtual void GrantFailed(string productId, OrderPath path, Exception error)
        {
            Debug.LogError(Tag + "Grant failed (" + path + ") for " + Name(productId) + ": " + error);
        }

        public virtual void PurchaseFailed(string productId, PurchaseFailureKind kind, string details)
        {
            Debug.LogWarning(Tag + "Purchase failed for " + Name(productId) + ": " + kind + " (" + details + ")");
        }

        public virtual void EntitlementsRevoked(IReadOnlyList<string> productIds)
        {
            Debug.LogWarning(Tag + "No longer owned, revoked: " + string.Join(", ", productIds));
        }

        public virtual void EmptyOwnedFetchIgnored(int previouslyOwnedCount)
        {
            Debug.LogWarning(Tag + "The purchase fetch returned nothing although " + previouslyOwnedCount + " product(s) were owned before. Nothing was revoked.");
        }

        public virtual void InstallSourceDetected(string installerName, bool expected)
        {
            Debug.Log(Tag + "Installer: " + Name(installerName) + (expected ? string.Empty : " (not an expected store)"));
        }

        public virtual void Warning(string message)
        {
            Debug.LogWarning(Tag + message);
        }

        private static string Name(string value)
        {
            return string.IsNullOrEmpty(value) ? "(none)" : value;
        }
    }
}
