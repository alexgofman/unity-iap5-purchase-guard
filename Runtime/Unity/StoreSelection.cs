using UnityEngine;
using UnityEngine.Purchasing;

namespace Iap5PurchaseGuard
{
    /// <summary>
    /// Names the store explicitly instead of letting the SDK pick its default.
    ///
    /// When no store name is given on Android, Unity IAP 5 reads the store choice from a text
    /// asset packaged with the game. Anything other than Google Play in that asset, or no asset at
    /// all, selects the SDK's built-in test store, which completes purchases without a real store
    /// behind it. Editing that one asset in a repacked build is therefore enough to make every
    /// purchase "succeed". Passing the store name from code takes the asset out of the decision:
    /// the choice can then only be changed by patching compiled code.
    /// </summary>
    public static class StoreSelection
    {
        /// <summary>
        /// The store name to pass to <c>UnityIAPServices.StoreController</c>, or null to keep the
        /// SDK default (the Editor and platforms this package does not cover).
        /// </summary>
        public static string ForPlatform(RuntimePlatform platform)
        {
            switch (platform)
            {
                case RuntimePlatform.Android:
                    return GooglePlay.Name;
                case RuntimePlatform.IPhonePlayer:
                case RuntimePlatform.tvOS:
                    return AppleAppStore.Name;
                default:
                    return null;
            }
        }
    }
}
