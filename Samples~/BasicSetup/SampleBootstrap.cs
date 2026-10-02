using UnityEngine;
using UnityEngine.Purchasing;

namespace Iap5PurchaseGuard.Samples
{
    /// <summary>
    /// Add this to an empty object in an empty scene and press Play. In the Editor the SDK test
    /// store answers the purchases; on a device the real store does. The test store cannot
    /// restore, so in the Editor the Restore button only shows the store's refusal.
    /// </summary>
    public sealed class SampleBootstrap : MonoBehaviour, IPurchaseUi
    {
        private PurchasePipeline _pipeline;
        private SampleEntitlements _entitlements;
        private string _status = "Connecting to the store.";

        private void Start()
        {
            _entitlements = new SampleEntitlements();
            _pipeline = PurchasePipeline.Create(new PurchaseGuardSetup
            {
                Catalog = SampleCatalog.Create(),
                Granter = _entitlements,
                Ui = this,
                PlayLicensingPublicKeyBase64 = SampleKeys.PlayLicensingPublicKey,
                WhenValidationUnavailable = UnavailablePolicy.FailOpen
            });

            _pipeline.OwnedPurchasesSynced += report =>
                _status = "Owned purchases synced: " + report.Granted + " restored, " + report.Revoked.Count + " revoked.";
            _pipeline.RestoreFailed += reason => _status = "Restore failed: " + reason;
        }

        private void OnDestroy()
        {
            if (_pipeline != null)
            {
                // Shutdown takes effect at once, so a reloaded scene can create a new pipeline in
                // the same frame; Destroy alone only acts at the end of the frame.
                _pipeline.Shutdown();
                Destroy(_pipeline.gameObject);
            }
        }

        private void OnGUI()
        {
            if (_pipeline == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(20f, 20f, 440f, 420f));
            GUILayout.Label(_pipeline.IsReady ? "Store connected." : "Store not connected.");
            GUILayout.Label("Lantern oil: " + _entitlements.LanternOil);
            GUILayout.Label("Map expansion: " + Owned(SampleCatalog.MapExpansion));
            GUILayout.Label("Explorer club: " + Owned(SampleCatalog.ExplorerClub));

            BuyButton("Buy lantern oil", SampleCatalog.LanternOil);
            BuyButton("Buy map expansion", SampleCatalog.MapExpansion);
            BuyButton("Join explorer club", SampleCatalog.ExplorerClub);

            if (GUILayout.Button("Restore purchases"))
            {
                _status = "Restoring.";
                _pipeline.RestorePurchases();
            }

            GUILayout.Label(_status);
            GUILayout.EndArea();
        }

        private void BuyButton(string label, string productId)
        {
            Product product = _pipeline.FindProduct(productId);
            string price = product != null && product.metadata != null ? product.metadata.localizedPriceString : "?";
            if (GUILayout.Button(label + " (" + price + ")"))
            {
                _status = "Buying " + productId + ".";
                _pipeline.Buy(productId);
            }
        }

        private string Owned(string productId)
        {
            return _entitlements.IsEntitlementActive(productId) ? "owned" : "not owned";
        }

        public void PurchaseSucceeded(string productId)
        {
            _status = "Unlocked " + productId + ".";
        }

        public void PurchaseRefused(string productId, VerdictReason reason)
        {
            _status = "The purchase could not be verified, so nothing was unlocked (" + reason + ").";
            if (!_pipeline.InstallReport.IsExpected)
            {
                _status += " Official version: " + InstallSource.PlayStoreListingUrl();
            }
        }

        public void PurchasePending(string productId)
        {
            _status = "The store is still processing the payment for " + productId + ". It unlocks when the payment completes.";
        }

        public void PurchaseFailed(string productId, PurchaseFailureKind kind)
        {
            _status = kind == PurchaseFailureKind.Cancelled ? "The purchase was cancelled." : "Purchase failed: " + kind + ".";
        }
    }
}
