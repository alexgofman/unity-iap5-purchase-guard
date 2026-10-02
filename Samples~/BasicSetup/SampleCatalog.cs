namespace Iap5PurchaseGuard.Samples
{
    /// <summary>
    /// A made-up catalogue with one product of each kind. Replace the ids with the ones registered
    /// in Play Console and App Store Connect.
    /// </summary>
    public static class SampleCatalog
    {
        public const string LanternOil = "sample.lantern_oil_100";
        public const string MapExpansion = "sample.map_expansion";
        public const string ExplorerClub = "sample.explorer_club_monthly";

        public static PurchaseCatalog Create()
        {
            return new PurchaseCatalog(new[]
            {
                new CatalogEntry(LanternOil, ProductKind.Consumable),
                new CatalogEntry(MapExpansion, ProductKind.NonConsumable),
                new CatalogEntry(ExplorerClub, ProductKind.Subscription)
            });
        }
    }

    /// <summary>
    /// Where the Google Play licensing public key goes: Play Console, Monetization setup,
    /// Licensing. Keep it as a constant in code. A key read from an asset or a remote setting can
    /// be blanked in a repacked build without touching code.
    /// </summary>
    public static class SampleKeys
    {
        public const string PlayLicensingPublicKey = PlayLicenseKey.Placeholder;
    }
}
