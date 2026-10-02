using System;
using System.Collections.Generic;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;

namespace Iap5PurchaseGuard
{
    /// <summary>
    /// Trust gate for Google Play. Unity IAP's <c>CrossPlatformValidator</c> checks the RSA
    /// signature of the purchase data against the app's Play licensing public key, and
    /// <see cref="PlayReceiptRules"/> turns the result into a verdict.
    ///
    /// Google Play signs purchase data with a private key only it holds. A billing client that
    /// fabricates purchases cannot produce data that verifies against the app's licensing key, so
    /// its orders come back as <see cref="ReceiptVerdict.Invalid"/>.
    ///
    /// The licensing key is public by design: it only lets the app check a signature. What matters
    /// is where it comes from. Pass it from compiled code. A key loaded from an asset, a config
    /// file or a remote setting can be blanked in a repacked build without touching code, and a
    /// blank key means <see cref="ReceiptVerdict.Unavailable"/>, which is the verdict that can
    /// fail open.
    ///
    /// Key states:
    /// <list type="bullet">
    /// <item>No key, or the sample placeholder: Unavailable (KeyNotConfigured).</item>
    /// <item>A key that does not parse: Unavailable (KeyUnusable).</item>
    /// <item>A well-formed key that is not this app's: every genuine receipt is refused as a
    /// signature mismatch, the orders are never confirmed and Google Play refunds them. Check the
    /// key with one licence-tester purchase from a Play testing track before release.</item>
    /// </list>
    /// </summary>
    public sealed class PlayReceiptValidator : IReceiptGate, IPlaySignatureCheck
    {
        private readonly string _licensingKeyBase64;
        private readonly string _applicationId;
        private CrossPlatformValidator _validator;
        private PlaySignatureStatus _keyStatus;
        private bool _keyLoaded;

        /// <param name="licensingKeyBase64">
        /// Play Console licensing key: the base64 text of the DER-encoded RSA public key.
        /// </param>
        /// <param name="applicationId">The application identifier the signed data must name.</param>
        public PlayReceiptValidator(string licensingKeyBase64, string applicationId)
        {
            _licensingKeyBase64 = licensingKeyBase64;
            _applicationId = applicationId ?? string.Empty;
        }

        public ReceiptCheck Check(IStoreOrder order)
        {
            return PlayReceiptRules.Evaluate(order, GooglePlay.Name, this);
        }

        public PlaySignatureResult Verify(string receipt)
        {
            // The key is loaded before the receipt is looked at, so the two statuses that can fail
            // open depend on the app's configuration alone.
            CrossPlatformValidator validator = LoadKey();
            if (validator == null)
            {
                return new PlaySignatureResult(_keyStatus);
            }

            IPurchaseReceipt[] receipts;
            try
            {
                receipts = validator.Validate(receipt);
            }
            catch (InvalidSignatureException)
            {
                return new PlaySignatureResult(PlaySignatureStatus.SignatureMismatch);
            }
            catch (IAPSecurityException error)
            {
                return new PlaySignatureResult(StatusFor(error));
            }
            catch (Exception)
            {
                // The receipt is data the other side controls. A failure nobody anticipated is a
                // rejection, never a pass.
                return new PlaySignatureResult(PlaySignatureStatus.Rejected);
            }

            var purchases = new List<PlayPurchase>();
            if (receipts != null)
            {
                foreach (IPurchaseReceipt entry in receipts)
                {
                    if (entry is GooglePlayReceipt playReceipt)
                    {
                        purchases.Add(new PlayPurchase(
                            playReceipt.productID,
                            playReceipt.purchaseToken,
                            playReceipt.purchaseState == GooglePurchaseState.Purchased));
                    }
                }
            }

            return new PlaySignatureResult(PlaySignatureStatus.Verified, purchases);
        }

        private CrossPlatformValidator LoadKey()
        {
            if (_keyLoaded)
            {
                return _validator;
            }

            _keyLoaded = true;
            if (!PlayLicenseKey.IsConfigured(_licensingKeyBase64))
            {
                _keyStatus = PlaySignatureStatus.KeyNotConfigured;
                return null;
            }

            try
            {
                byte[] publicKey = Convert.FromBase64String(_licensingKeyBase64.Trim());
                _validator = new CrossPlatformValidator(publicKey, _applicationId);
            }
            catch (Exception)
            {
                _validator = null;
                _keyStatus = PlaySignatureStatus.KeyUnusable;
            }

            return _validator;
        }

        // The validator's more specific exception types are declared in an assembly that is only
        // compiled for device builds, so they are recognised by name. That keeps this file
        // compiling in the Editor, and it only affects the reported reason: every exception from
        // the validator is a refusal.
        private static PlaySignatureStatus StatusFor(IAPSecurityException error)
        {
            switch (error.GetType().Name)
            {
                case "InvalidBundleIdException":
                    return PlaySignatureStatus.WrongApplication;
                case "InvalidReceiptDataException":
                    return PlaySignatureStatus.ReceiptUnreadable;
                default:
                    return PlaySignatureStatus.Rejected;
            }
        }
    }
}
