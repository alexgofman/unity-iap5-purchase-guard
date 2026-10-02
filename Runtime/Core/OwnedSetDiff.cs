using System;
using System.Collections.Generic;

namespace Iap5PurchaseGuard
{
    /// <summary>Result of comparing the previously owned products with the set a fetch just returned.</summary>
    public sealed class OwnedSetComparison
    {
        private static readonly string[] Nothing = new string[0];

        internal OwnedSetComparison(bool emptyFetchIgnored, IReadOnlyList<string> noLongerOwned)
        {
            EmptyFetchIgnored = emptyFetchIgnored;
            NoLongerOwned = noLongerOwned ?? Nothing;
        }

        /// <summary>
        /// True when the fetch returned nothing although products were owned before. Nothing is
        /// revoked in that case and the baseline must be kept as it is.
        /// </summary>
        public bool EmptyFetchIgnored { get; }

        /// <summary>Products that were owned before and are missing now, in ordinal order.</summary>
        public IReadOnlyList<string> NoLongerOwned { get; }
    }

    /// <summary>
    /// Refund detection by owned-set diff.
    ///
    /// Each successful purchase fetch reports the durable products the store account owns. A
    /// product that was in the previous report and is missing from the current one is no longer
    /// owned (refunded, charged back, or an expired subscription), so its entitlement can be
    /// taken back.
    ///
    /// The guard: a fetch that returns nothing is not evidence that everything was refunded. A
    /// signed-out or switched store account, a store cache that was just cleared and a tampered
    /// billing client all give the same empty answer. Reading it as "everything refunded" would
    /// strip real purchases from paying players, so an empty result revokes nothing and does not
    /// replace the baseline. The price is that the refund of a player's only product is not
    /// noticed by this heuristic. A server-side refund feed is the reliable way to catch that.
    ///
    /// This class only compares two sets. <see cref="PurchaseFlow"/> adds the second safeguard:
    /// a product has to be missing from two complete fetches in a row before it is revoked.
    /// </summary>
    public static class OwnedSetDiff
    {
        public static OwnedSetComparison Compare(ICollection<string> baseline, ICollection<string> current)
        {
            int baselineCount = baseline == null ? 0 : baseline.Count;
            int currentCount = current == null ? 0 : current.Count;

            if (baselineCount == 0)
            {
                // First report on this device: there is nothing to compare against.
                return new OwnedSetComparison(false, null);
            }

            if (currentCount == 0)
            {
                return new OwnedSetComparison(true, null);
            }

            var currentSet = new HashSet<string>(current, StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (string productId in baseline)
            {
                if (!string.IsNullOrEmpty(productId) && !currentSet.Contains(productId) && !missing.Contains(productId))
                {
                    missing.Add(productId);
                }
            }

            missing.Sort(StringComparer.Ordinal);
            return new OwnedSetComparison(false, missing);
        }
    }

    /// <summary>
    /// What is remembered between purchase fetches: the products owned at the last complete fetch,
    /// which <see cref="OwnedSetDiff"/> compares against, and the products that were missing from
    /// that fetch for the first time and are waiting for a second fetch to confirm it.
    /// </summary>
    public sealed class OwnedSetBaseline
    {
        private const char Separator = '\n';
        private const string MissingSuffix = ".missing";

        private readonly IStringStore _store;
        private readonly string _storageKey;

        public OwnedSetBaseline(IStringStore store, string storageKey)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (string.IsNullOrEmpty(storageKey))
            {
                throw new ArgumentException("A storage key is required.", nameof(storageKey));
            }

            _store = store;
            _storageKey = storageKey;
        }

        /// <summary>The products owned at the last complete fetch.</summary>
        public HashSet<string> Load()
        {
            return Read(_storageKey);
        }

        /// <summary>The products that were missing from the last complete fetch for the first time.</summary>
        public HashSet<string> LoadMissingOnce()
        {
            return Read(_storageKey + MissingSuffix);
        }

        public void Save(IEnumerable<string> owned, IEnumerable<string> missingOnce = null)
        {
            Write(_storageKey, owned);
            Write(_storageKey + MissingSuffix, missingOnce);
        }

        private HashSet<string> Read(string key)
        {
            var productIds = new HashSet<string>(StringComparer.Ordinal);
            string stored = _store.Read(key);
            if (string.IsNullOrEmpty(stored))
            {
                return productIds;
            }

            foreach (string entry in stored.Split(Separator))
            {
                string productId = entry.Trim();
                if (productId.Length > 0)
                {
                    productIds.Add(productId);
                }
            }

            return productIds;
        }

        private void Write(string key, IEnumerable<string> productIds)
        {
            var sorted = new List<string>();
            if (productIds != null)
            {
                foreach (string productId in productIds)
                {
                    if (!string.IsNullOrEmpty(productId) && !sorted.Contains(productId))
                    {
                        sorted.Add(productId);
                    }
                }
            }

            sorted.Sort(StringComparer.Ordinal);
            _store.Write(key, string.Join(Separator.ToString(), sorted));
        }
    }
}
