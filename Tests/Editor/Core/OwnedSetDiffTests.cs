using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Iap5PurchaseGuard.Tests
{
    [TestFixture]
    public class OwnedSetDiffTests
    {
        private static readonly string[] None = new string[0];

        [Test]
        public void NoBaseline_MeansNothingToRevoke()
        {
            OwnedSetComparison comparison = OwnedSetDiff.Compare(None, new[] { "a", "b" });

            Assert.That(comparison.EmptyFetchIgnored, Is.False);
            Assert.That(comparison.NoLongerOwned, Is.Empty);
        }

        [Test]
        public void SameSet_MeansNothingToRevoke()
        {
            OwnedSetComparison comparison = OwnedSetDiff.Compare(new[] { "a", "b" }, new[] { "b", "a" });

            Assert.That(comparison.EmptyFetchIgnored, Is.False);
            Assert.That(comparison.NoLongerOwned, Is.Empty);
        }

        [Test]
        public void MissingProducts_AreReportedInOrdinalOrder()
        {
            OwnedSetComparison comparison = OwnedSetDiff.Compare(new[] { "c", "a", "b" }, new[] { "b" });

            Assert.That(comparison.NoLongerOwned, Is.EqualTo(new[] { "a", "c" }));
        }

        [Test]
        public void NewProducts_AreNotRevocations()
        {
            OwnedSetComparison comparison = OwnedSetDiff.Compare(new[] { "a" }, new[] { "a", "b" });

            Assert.That(comparison.NoLongerOwned, Is.Empty);
        }

        [Test]
        public void EmptyFetch_IsNotReadAsEverythingRefunded()
        {
            OwnedSetComparison comparison = OwnedSetDiff.Compare(new[] { "a", "b" }, None);

            Assert.That(comparison.EmptyFetchIgnored, Is.True);
            Assert.That(comparison.NoLongerOwned, Is.Empty);
        }

        [Test]
        public void NullSets_AreTreatedAsEmpty()
        {
            Assert.That(OwnedSetDiff.Compare(null, null).EmptyFetchIgnored, Is.False);
            Assert.That(OwnedSetDiff.Compare(new[] { "a" }, null).EmptyFetchIgnored, Is.True);
            Assert.That(OwnedSetDiff.Compare(null, new[] { "a" }).NoLongerOwned, Is.Empty);
        }

        [Test]
        public void Baseline_RoundTripsThroughStorage()
        {
            var storage = new InMemoryStringStore();
            var baseline = new OwnedSetBaseline(storage, "test.owned");

            baseline.Save(new[] { "b", "a", "b", string.Empty });
            HashSet<string> loaded = new OwnedSetBaseline(storage, "test.owned").Load();

            Assert.That(loaded, Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(storage.Read("test.owned"), Is.EqualTo("a\nb"));
        }

        [Test]
        public void Baseline_KeepsTheMissingOnceSetApart()
        {
            var storage = new InMemoryStringStore();
            new OwnedSetBaseline(storage, "test.owned").Save(new[] { "a", "b" }, new[] { "b" });

            var reloaded = new OwnedSetBaseline(storage, "test.owned");

            Assert.That(reloaded.Load(), Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(reloaded.LoadMissingOnce(), Is.EquivalentTo(new[] { "b" }));
        }

        [Test]
        public void Baseline_SavedWithoutAMissingSet_ClearsIt()
        {
            var storage = new InMemoryStringStore();
            var baseline = new OwnedSetBaseline(storage, "test.owned");
            baseline.Save(new[] { "a", "b" }, new[] { "b" });

            baseline.Save(new[] { "a" });

            Assert.That(baseline.LoadMissingOnce(), Is.Empty);
        }

        [Test]
        public void Baseline_IsEmptyBeforeTheFirstSave()
        {
            Assert.That(new OwnedSetBaseline(new InMemoryStringStore(), "test.owned").Load(), Is.Empty);
        }

        [Test]
        public void Baseline_RejectsUnusableArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new OwnedSetBaseline(null, "test.owned"));
            Assert.Throws<ArgumentException>(() => new OwnedSetBaseline(new InMemoryStringStore(), string.Empty));
        }
    }
}
