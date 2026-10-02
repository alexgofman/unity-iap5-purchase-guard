using System;
using System.Collections.Generic;

namespace Iap5PurchaseGuard
{
    /// <summary>How a product behaves once bought.</summary>
    public enum ProductKind
    {
        /// <summary>Used up when granted. Granted on the purchase path only, never restored.</summary>
        Consumable = 0,

        /// <summary>Owned permanently. Restorable, and part of the owned set.</summary>
        NonConsumable = 1,

        /// <summary>Owned while the subscription is active. Restorable, and part of the owned set.</summary>
        Subscription = 2
    }

    /// <summary>One product the game sells.</summary>
    public sealed class CatalogEntry
    {
        public CatalogEntry(string productId, ProductKind kind)
        {
            if (string.IsNullOrEmpty(productId))
            {
                throw new ArgumentException("A product id is required.", nameof(productId));
            }

            ProductId = productId;
            Kind = kind;
        }

        public string ProductId { get; }

        public ProductKind Kind { get; }
    }

    /// <summary>
    /// The products the purchase flow is allowed to grant. An order for anything else is treated as
    /// unidentified: it is neither granted nor confirmed.
    /// </summary>
    public sealed class PurchaseCatalog
    {
        private readonly List<CatalogEntry> _entries = new List<CatalogEntry>();
        private readonly Dictionary<string, CatalogEntry> _byId = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);

        public PurchaseCatalog(IEnumerable<CatalogEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            foreach (CatalogEntry entry in entries)
            {
                if (entry == null)
                {
                    throw new ArgumentException("A catalogue entry is null.", nameof(entries));
                }

                if (_byId.ContainsKey(entry.ProductId))
                {
                    throw new ArgumentException("Product id listed twice: " + entry.ProductId, nameof(entries));
                }

                _byId.Add(entry.ProductId, entry);
                _entries.Add(entry);
            }
        }

        public IReadOnlyList<CatalogEntry> Entries
        {
            get { return _entries; }
        }

        public bool Contains(string productId)
        {
            return !string.IsNullOrEmpty(productId) && _byId.ContainsKey(productId);
        }

        public bool TryGet(string productId, out CatalogEntry entry)
        {
            if (string.IsNullOrEmpty(productId))
            {
                entry = null;
                return false;
            }

            return _byId.TryGetValue(productId, out entry);
        }
    }
}
