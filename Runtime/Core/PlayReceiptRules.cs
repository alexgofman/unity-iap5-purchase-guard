using System;
using System.Collections.Generic;

namespace Iap5PurchaseGuard
{
    /// <summary>Outcome of checking the Google Play signature on a receipt.</summary>
    /// <remarks>Rejected is the zero value, so the default fails closed.</remarks>
    public enum PlaySignatureStatus
    {
        /// <summary>The check failed for a reason not listed below.</summary>
        Rejected = 0,

        /// <summary>The purchase data was signed with the app's licensing key.</summary>
        Verified,

        /// <summary>No licensing key was supplied.</summary>
        KeyNotConfigured,

        /// <summary>A licensing key was supplied but could not be loaded.</summary>
        KeyUnusable,

        /// <summary>The signature does not match the purchase data.</summary>
        SignatureMismatch,

        /// <summary>The signed data names another application.</summary>
        WrongApplication,

        /// <summary>The receipt could not be parsed.</summary>
        ReceiptUnreadable
    }

    /// <summary>One purchase found in store-signed data.</summary>
    public readonly struct PlayPurchase
    {
        public PlayPurchase(string storeProductId, string purchaseToken, bool isPurchased)
        {
            StoreProductId = storeProductId ?? string.Empty;
            PurchaseToken = purchaseToken ?? string.Empty;
            IsPurchased = isPurchased;
        }

        /// <summary>The product id inside the signed data.</summary>
        public string StoreProductId { get; }

        /// <summary>The purchase token inside the signed data.</summary>
        public string PurchaseToken { get; }

        /// <summary>False when the signed state is pending, cancelled or refunded.</summary>
        public bool IsPurchased { get; }
    }

    /// <summary>What the signature check found.</summary>
    public sealed class PlaySignatureResult
    {
        private static readonly PlayPurchase[] NoPurchases = new PlayPurchase[0];

        public PlaySignatureResult(PlaySignatureStatus status, IReadOnlyList<PlayPurchase> purchases = null)
        {
            Status = status;
            Purchases = purchases ?? NoPurchases;
        }

        public PlaySignatureStatus Status { get; }

        /// <summary>The signed purchases. Never null; only meaningful when the status is Verified.</summary>
        public IReadOnlyList<PlayPurchase> Purchases { get; }
    }

    /// <summary>
    /// The cryptographic part of Play receipt validation, kept behind an interface so the decision
    /// rules in <see cref="PlayReceiptRules"/> can be tested without the store SDK.
    /// </summary>
    public interface IPlaySignatureCheck
    {
        /// <summary>
        /// Verifies a receipt. <see cref="PlaySignatureStatus.KeyNotConfigured"/> and
        /// <see cref="PlaySignatureStatus.KeyUnusable"/> must depend on the key alone and be
        /// decided before the receipt is read, because they are the statuses that can fail open.
        /// </summary>
        PlaySignatureResult Verify(string receipt);
    }

    /// <summary>
    /// Turns the facts about a Google Play order into a verdict.
    ///
    /// The structural checks run first and need no key: the order must carry a transaction id
    /// (Google Play always supplies a purchase token), a receipt must be present, the receipt must
    /// be stamped by the expected store, and the order must name a product. They still hold when the
    /// signature check is unavailable, and nothing written into a receipt can route an order
    /// around them into the fail-open branch.
    ///
    /// After the signature check, the signed data has to contain a purchase for the product in the
    /// order, with the order's own transaction id as its purchase token, in the purchased state. A
    /// verified result that contains no purchase proves nothing about this order and is refused.
    ///
    /// The token comparison is what ties the ledger to the signature. Without it, purchase data
    /// from one real purchase could be presented again and again under invented transaction ids:
    /// the signature would verify each time, and each new id would get past the ledger.
    /// </summary>
    public static class PlayReceiptRules
    {
        public static ReceiptCheck Evaluate(IStoreOrder order, string expectedStoreName, IPlaySignatureCheck signatureCheck)
        {
            if (signatureCheck == null)
            {
                throw new ArgumentNullException(nameof(signatureCheck));
            }

            if (order == null)
            {
                return ReceiptCheck.Invalid(VerdictReason.ReceiptMissing);
            }

            if (string.IsNullOrEmpty(order.TransactionId))
            {
                return ReceiptCheck.Invalid(VerdictReason.TransactionIdMissing);
            }

            if (string.IsNullOrEmpty(order.Receipt))
            {
                return ReceiptCheck.Invalid(VerdictReason.ReceiptMissing);
            }

            if (string.IsNullOrEmpty(expectedStoreName) || !string.Equals(order.StoreName, expectedStoreName, StringComparison.Ordinal))
            {
                return ReceiptCheck.Invalid(VerdictReason.WrongStore);
            }

            if (string.IsNullOrEmpty(order.ProductId) && string.IsNullOrEmpty(order.StoreProductId))
            {
                return ReceiptCheck.Invalid(VerdictReason.ProductUnknown);
            }

            PlaySignatureResult result = signatureCheck.Verify(order.Receipt);
            if (result == null)
            {
                return ReceiptCheck.Invalid(VerdictReason.ValidatorRejected);
            }

            switch (result.Status)
            {
                case PlaySignatureStatus.Verified:
                    break;
                case PlaySignatureStatus.KeyNotConfigured:
                    return ReceiptCheck.Unavailable(VerdictReason.KeyNotConfigured);
                case PlaySignatureStatus.KeyUnusable:
                    return ReceiptCheck.Unavailable(VerdictReason.KeyUnusable);
                case PlaySignatureStatus.SignatureMismatch:
                    return ReceiptCheck.Invalid(VerdictReason.SignatureMismatch);
                case PlaySignatureStatus.WrongApplication:
                    return ReceiptCheck.Invalid(VerdictReason.WrongApplication);
                case PlaySignatureStatus.ReceiptUnreadable:
                    return ReceiptCheck.Invalid(VerdictReason.ReceiptUnreadable);
                default:
                    return ReceiptCheck.Invalid(VerdictReason.ValidatorRejected);
            }

            if (result.Purchases.Count == 0)
            {
                return ReceiptCheck.Invalid(VerdictReason.NoStoreReceipt);
            }

            foreach (PlayPurchase purchase in result.Purchases)
            {
                // Correctly signed, but for another product: a real receipt replayed against this order.
                if (!NamesProductOf(order, purchase.StoreProductId))
                {
                    return ReceiptCheck.Invalid(VerdictReason.WrongProduct);
                }

                // Correctly signed and for this product, but for another transaction: a real receipt
                // replayed under a new transaction id.
                if (!string.Equals(purchase.PurchaseToken, order.TransactionId, StringComparison.Ordinal))
                {
                    return ReceiptCheck.Invalid(VerdictReason.WrongTransaction);
                }

                // Correctly signed and for this product, but not paid. This is real store data, not
                // forgery, so it is held rather than refused.
                if (!purchase.IsPurchased)
                {
                    return ReceiptCheck.Deferred(VerdictReason.NotPurchased);
                }
            }

            return ReceiptCheck.Valid(VerdictReason.SignatureVerified);
        }

        // A receipt carries the store's product id. The catalogue id is only used when the order
        // does not say what the store id is.
        private static bool NamesProductOf(IStoreOrder order, string signedProductId)
        {
            if (string.IsNullOrEmpty(signedProductId))
            {
                return false;
            }

            string expected = string.IsNullOrEmpty(order.StoreProductId) ? order.ProductId : order.StoreProductId;
            return string.Equals(signedProductId, expected, StringComparison.Ordinal);
        }
    }

    /// <summary>Helpers for the Google Play licensing public key.</summary>
    public static class PlayLicenseKey
    {
        /// <summary>Stand-in used by the sample. A key equal to it counts as not configured.</summary>
        public const string Placeholder = "REPLACE_WITH_YOUR_PLAY_LICENSING_PUBLIC_KEY";

        public static bool IsConfigured(string keyBase64)
        {
            return !string.IsNullOrWhiteSpace(keyBase64)
                && !string.Equals(keyBase64.Trim(), Placeholder, StringComparison.Ordinal);
        }
    }
}
