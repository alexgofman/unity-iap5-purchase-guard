using UnityEngine;

namespace Iap5PurchaseGuard
{
    /// <summary>Picks the trust gate for the platform the game is running on.</summary>
    public static class ReceiptGates
    {
        /// <summary>
        /// Android: <see cref="PlayReceiptValidator"/>, the only place where this package checks a
        /// signature itself.
        ///
        /// iOS and tvOS 15 and later: Unity IAP 5 uses StoreKit 2 there, which verifies each signed
        /// transaction on the device. This package adds no receipt check of its own on Apple
        /// platforms; it returns Valid and relies on StoreKit and the SDK.
        ///
        /// iOS and tvOS below 15: Unity IAP falls back to StoreKit 1, where nothing verifies a
        /// transaction on the device. The gate still returns Valid, with a reason that says
        /// nothing was checked. See <see cref="AppleSystem"/>.
        ///
        /// Everything else, including the Editor: there is no store signature to check, so the
        /// gate returns Valid. The SDK test store used in the Editor signs nothing.
        /// </summary>
        public static IReceiptGate ForPlatform(
            RuntimePlatform platform,
            string playLicensingKeyBase64,
            string applicationId,
            bool storeKit1 = false)
        {
            switch (platform)
            {
                case RuntimePlatform.Android:
                    return new PlayReceiptValidator(playLicensingKeyBase64, applicationId);
                case RuntimePlatform.IPhonePlayer:
                case RuntimePlatform.tvOS:
                    return new FixedVerdictGate(ReceiptCheck.Valid(
                        storeKit1 ? VerdictReason.NotEnforcedOnThisPlatform : VerdictReason.VerifiedByPlatform));
                default:
                    return new FixedVerdictGate(ReceiptCheck.Valid(VerdictReason.NotEnforcedOnThisPlatform));
            }
        }
    }

    /// <summary>A gate that gives every order the same verdict.</summary>
    public sealed class FixedVerdictGate : IReceiptGate
    {
        private readonly ReceiptCheck _check;

        public FixedVerdictGate(ReceiptCheck check)
        {
            _check = check;
        }

        public ReceiptCheck Check(IStoreOrder order)
        {
            return _check;
        }
    }
}
