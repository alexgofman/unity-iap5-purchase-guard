using System;
using NUnit.Framework;

namespace Iap5PurchaseGuard.Tests
{
    [TestFixture]
    public class PurchaseCatalogTests
    {
        [Test]
        public void KnowsItsProducts()
        {
            PurchaseCatalog catalog = TestCatalog.Create();

            Assert.That(catalog.Entries, Has.Count.EqualTo(3));
            Assert.That(catalog.Contains(TestCatalog.LanternOil), Is.True);
            Assert.That(catalog.TryGet(TestCatalog.ExplorerClub, out CatalogEntry entry), Is.True);
            Assert.That(entry.Kind, Is.EqualTo(ProductKind.Subscription));
        }

        [Test]
        public void UnknownAndEmptyIds_AreNotInTheCatalogue()
        {
            PurchaseCatalog catalog = TestCatalog.Create();

            Assert.That(catalog.Contains("retired_product"), Is.False);
            Assert.That(catalog.Contains(string.Empty), Is.False);
            Assert.That(catalog.Contains(null), Is.False);
            Assert.That(catalog.TryGet(null, out CatalogEntry entry), Is.False);
            Assert.That(entry, Is.Null);
        }

        [Test]
        public void ProductIdsAreCaseSensitive()
        {
            Assert.That(TestCatalog.Create().Contains(TestCatalog.LanternOil.ToUpperInvariant()), Is.False);
        }

        [Test]
        public void RejectsDuplicatesAndBlanks()
        {
            Assert.Throws<ArgumentNullException>(() => new PurchaseCatalog(null));
            Assert.Throws<ArgumentException>(() => new CatalogEntry(string.Empty, ProductKind.Consumable));
            Assert.Throws<ArgumentException>(() => new PurchaseCatalog(new CatalogEntry[] { null }));
            Assert.Throws<ArgumentException>(() => new PurchaseCatalog(new[]
            {
                new CatalogEntry(TestCatalog.MapExpansion, ProductKind.NonConsumable),
                new CatalogEntry(TestCatalog.MapExpansion, ProductKind.NonConsumable)
            }));
        }
    }
}
