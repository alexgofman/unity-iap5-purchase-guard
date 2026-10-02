using System;
using UnityEngine;

namespace Iap5PurchaseGuard.Samples
{
    /// <summary>
    /// The game's side of a purchase, kept in PlayerPrefs to stay short. A real game applies these
    /// to its own save data.
    /// </summary>
    public sealed class SampleEntitlements : IEntitlementGranter
    {
        private const string OilKey = "purchase_guard_sample.oil";
        private const string OwnsPrefix = "purchase_guard_sample.owns.";

        public int LanternOil
        {
            get { return PlayerPrefs.GetInt(OilKey, 0); }
        }

        public bool IsEntitlementActive(string productId)
        {
            return PlayerPrefs.GetInt(OwnsPrefix + productId, 0) == 1;
        }

        public void Grant(string productId, string transactionId, GrantSource source)
        {
            switch (productId)
            {
                case SampleCatalog.LanternOil:
                    PlayerPrefs.SetInt(OilKey, LanternOil + 100);
                    break;
                case SampleCatalog.MapExpansion:
                case SampleCatalog.ExplorerClub:
                    PlayerPrefs.SetInt(OwnsPrefix + productId, 1);
                    break;
                default:
                    // Throwing leaves the order unconfirmed, so nothing is lost if a product is
                    // added to the catalogue before it is handled here.
                    throw new ArgumentException("No grant is defined for " + productId, nameof(productId));
            }

            PlayerPrefs.Save();
        }

        public void Revoke(string productId)
        {
            PlayerPrefs.DeleteKey(OwnsPrefix + productId);
            PlayerPrefs.Save();
        }
    }
}
