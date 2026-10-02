using System;
using System.Collections.Generic;
using UnityEngine.Purchasing;

namespace Iap5PurchaseGuard
{
    /// <summary>Turns the package's catalogue into the product definitions Unity IAP fetches.</summary>
    public static class CatalogMapping
    {
        public static List<ProductDefinition> ToProductDefinitions(this PurchaseCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var definitions = new List<ProductDefinition>(catalog.Entries.Count);
            foreach (CatalogEntry entry in catalog.Entries)
            {
                definitions.Add(new ProductDefinition(entry.ProductId, ToProductType(entry.Kind)));
            }

            return definitions;
        }

        public static ProductType ToProductType(ProductKind kind)
        {
            switch (kind)
            {
                case ProductKind.Consumable:
                    return ProductType.Consumable;
                case ProductKind.Subscription:
                    return ProductType.Subscription;
                default:
                    return ProductType.NonConsumable;
            }
        }
    }
}
