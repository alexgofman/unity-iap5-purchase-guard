using System.Collections.Generic;
using NUnit.Framework;

namespace Iap5PurchaseGuard.Tests
{
    [TestFixture]
    public class OwnedSyncTests
    {
        private const string Map = TestCatalog.MapExpansion;
        private const string Club = TestCatalog.ExplorerClub;

        private static IReadOnlyList<IStoreOrder> Orders(params FakeOrder[] orders)
        {
            return orders;
        }

        private static IReadOnlyList<IStoreOrder> MapAndClub()
        {
            return Orders(Harness.Owned(Map, "tx-map"), Harness.Owned(Club, "tx-club"));
        }

        private static IReadOnlyList<IStoreOrder> MapOnly()
        {
            return Orders(Harness.Owned(Map, "tx-map"));
        }

        // ---- granting what is owned ----

        [Test]
        public void OwnedProductThatIsNotActive_IsGrantedAsARestore()
        {
            var h = new Harness();

            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly());

            Assert.That(report.Granted, Is.EqualTo(1));
            Assert.That(h.Granter.Grants, Is.EqualTo(new[] { Map + "/Restore" }));
            Assert.That(h.Granter.GrantedTransactions, Is.EqualTo(new[] { "tx-map" }));
            Assert.That(h.Store.ConfirmCalls, Is.EqualTo(0), "an owned order is already confirmed");
            Assert.That(h.Telemetry.Count("granted:"), Is.EqualTo(0), "a restore is not a sale");
        }

        [Test]
        public void OwnedProductThatIsActive_IsNotGrantedAgainOnEveryFetch()
        {
            var h = new Harness();

            h.Flow.ProcessOwned(MapOnly());
            OwnedSyncReport second = h.Flow.ProcessOwned(MapOnly());
            h.Relaunch();
            OwnedSyncReport third = h.Flow.ProcessOwned(MapOnly());

            Assert.That(h.Granter.Grants, Has.Count.EqualTo(1));
            Assert.That(second.AlreadyActive, Is.EqualTo(1));
            Assert.That(third.AlreadyActive, Is.EqualTo(1));
            Assert.That(third.Granted, Is.EqualTo(0));
        }

        [Test]
        public void OwnedProductWhoseEntitlementWasLost_IsGrantedAgain()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapOnly());

            // The game's save was reset, but the ledger and the baseline survived.
            h.Granter.Active.Clear();
            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly());

            Assert.That(report.Granted, Is.EqualTo(1));
            Assert.That(h.Granter.Grants, Has.Count.EqualTo(2));
        }

        [Test]
        public void InvalidOwnedOrder_IsNotGrantedAndNotCountedAsOwned()
        {
            var h = new Harness();
            h.Gate.Verdict = ReceiptCheck.Invalid(VerdictReason.SignatureMismatch);

            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly());

            Assert.That(report.Refused, Is.EqualTo(1));
            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Storage.Read(Harness.OwnedKey), Is.Empty);
            Assert.That(h.Telemetry.Events, Is.EqualTo(new[] { "refused:" + Map + ":SignatureMismatch:Restore" }));
            Assert.That(h.Ui.Messages, Is.Empty, "nobody is waiting on a paywall during a sync");
        }

        [Test]
        public void DeferredOwnedOrder_IsNotGranted()
        {
            var h = new Harness();
            h.Gate.Verdict = ReceiptCheck.Deferred(VerdictReason.NotPurchased);

            OwnedSyncReport report = h.Flow.ProcessOwned(Orders(Harness.Owned(Club)));

            Assert.That(report.Waiting, Is.EqualTo(1));
            Assert.That(h.Granter.Grants, Is.Empty);
        }

        [Test]
        public void ConsumableReportedAsOwned_IsIgnored()
        {
            var h = new Harness();

            OwnedSyncReport report = h.Flow.ProcessOwned(Orders(Harness.Owned(TestCatalog.LanternOil)));

            Assert.That(report.Skipped, Is.EqualTo(1));
            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Gate.Calls, Is.EqualTo(0));
        }

        [Test]
        public void ProductOutsideTheCatalogueReportedAsOwned_IsIgnored()
        {
            var h = new Harness();

            OwnedSyncReport report = h.Flow.ProcessOwned(Orders(Harness.Owned("retired_product"), Harness.Owned(string.Empty)));

            Assert.That(report.Skipped, Is.EqualTo(2));
            Assert.That(h.Granter.Grants, Is.Empty);
        }

        [Test]
        public void TestStoreOwnedOrder_IsRefusedWhenTheCheckIsOn()
        {
            var h = new Harness(rejectTestStoreOrders: true);
            FakeOrder order = Harness.Owned(Map);
            order.StoreName = PurchaseFlowOptions.SdkTestStoreName;

            OwnedSyncReport report = h.Flow.ProcessOwned(Orders(order));

            Assert.That(report.Refused, Is.EqualTo(1));
            Assert.That(h.Granter.Grants, Is.Empty);
        }

        [Test]
        public void ThrowingRestoreGrant_IsCountedAndDoesNotStopTheOthers()
        {
            var h = new Harness();
            h.Granter.FailingGrants = 1;

            OwnedSyncReport report = h.Flow.ProcessOwned(MapAndClub());

            Assert.That(report.Failed, Is.EqualTo(1));
            Assert.That(report.Granted, Is.EqualTo(1));
            Assert.That(h.Granter.Grants, Is.EqualTo(new[] { Club + "/Restore" }));
            Assert.That(h.Telemetry.Count("grant-failed:" + Map + ":Restore"), Is.EqualTo(1));
        }

        [Test]
        public void UnavailableVerdict_WithFailOpen_CountsAsOwned()
        {
            var h = new Harness(UnavailablePolicy.FailOpen);
            h.Gate.Verdict = ReceiptCheck.Unavailable(VerdictReason.KeyNotConfigured);

            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly());

            Assert.That(report.Granted, Is.EqualTo(1));
            Assert.That(h.Storage.Read(Harness.OwnedKey), Is.EqualTo(Map));
            Assert.That(h.Telemetry.Count("unavailable:"), Is.EqualTo(1));
        }

        [Test]
        public void OwnedOrdersAreJudgedOneByOne()
        {
            var h = new Harness();
            h.Gate.VerdictByProduct[Club] = ReceiptCheck.Invalid(VerdictReason.WrongProduct);

            OwnedSyncReport report = h.Flow.ProcessOwned(MapAndClub());

            Assert.That(report.Granted, Is.EqualTo(1));
            Assert.That(report.Refused, Is.EqualTo(1));
            Assert.That(h.Granter.Grants, Is.EqualTo(new[] { Map + "/Restore" }));
        }

        // ---- taking away what is no longer owned ----

        [Test]
        public void FirstSync_RevokesNothing()
        {
            var h = new Harness();
            h.Granter.Active.Add(Club);

            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly());

            Assert.That(report.Revoked, Is.Empty);
            Assert.That(report.MissingOnce, Is.EqualTo(0));
            Assert.That(h.Granter.Revocations, Is.Empty);
            Assert.That(h.Storage.Read(Harness.OwnedKey), Is.EqualTo(Map));
        }

        [Test]
        public void ProductMissingFromOneFetch_IsNotRevokedYet()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapAndClub());

            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly());

            Assert.That(report.MissingOnce, Is.EqualTo(1));
            Assert.That(report.Revoked, Is.Empty);
            Assert.That(h.Granter.Revocations, Is.Empty);
            Assert.That(h.Granter.Active, Is.EquivalentTo(new[] { Map, Club }));
        }

        [Test]
        public void ProductMissingFromTwoFetchesInARow_IsRevokedOnce()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapAndClub());

            // The subscription lapsed or was refunded: the store stops reporting it.
            h.Flow.ProcessOwned(MapOnly());
            h.Relaunch();
            OwnedSyncReport second = h.Flow.ProcessOwned(MapOnly());
            OwnedSyncReport third = h.Flow.ProcessOwned(MapOnly());

            Assert.That(second.Revoked, Is.EqualTo(new[] { Club }));
            Assert.That(third.Revoked, Is.Empty);
            Assert.That(third.MissingOnce, Is.EqualTo(0));
            Assert.That(h.Granter.Revocations, Is.EqualTo(new[] { Club }));
            Assert.That(h.Granter.Active, Is.EquivalentTo(new[] { Map }));
            Assert.That(h.Telemetry.Count("revoked:" + Club), Is.EqualTo(1));
        }

        [Test]
        public void ProductThatComesBack_StartsFromScratch()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapAndClub());
            h.Flow.ProcessOwned(MapOnly());

            // It was only missing from one answer. It is back, so the count starts again.
            h.Flow.ProcessOwned(MapAndClub());
            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly());

            Assert.That(report.MissingOnce, Is.EqualTo(1));
            Assert.That(report.Revoked, Is.Empty);
            Assert.That(h.Granter.Revocations, Is.Empty);
        }

        [Test]
        public void EmptyFetch_DoesNotRevokeAnything_HoweverOftenItRepeats()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapAndClub());

            OwnedSyncReport first = h.Flow.ProcessOwned(Orders());
            OwnedSyncReport second = h.Flow.ProcessOwned(Orders());
            OwnedSyncReport third = h.Flow.ProcessOwned(null);

            Assert.That(first.EmptyFetchIgnored, Is.True);
            Assert.That(second.EmptyFetchIgnored, Is.True);
            Assert.That(third.EmptyFetchIgnored, Is.True);
            Assert.That(h.Granter.Revocations, Is.Empty);
            Assert.That(h.Granter.Active, Is.EquivalentTo(new[] { Map, Club }));
            Assert.That(h.Telemetry.Count("empty-fetch-ignored:2"), Is.EqualTo(3));
        }

        [Test]
        public void EmptyFetch_KeepsTheBaseline_SoALaterRefundIsStillSeen()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapAndClub());
            h.Flow.ProcessOwned(Orders());

            h.Flow.ProcessOwned(MapOnly());
            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly());

            Assert.That(report.Revoked, Is.EqualTo(new[] { Club }));
        }

        [Test]
        public void FetchWhereEveryOrderIsRefused_DoesNotRevokeAnything()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapOnly());

            // What a build with the wrong licensing key sees: genuine orders fail the signature check.
            h.Gate.Verdict = ReceiptCheck.Invalid(VerdictReason.SignatureMismatch);
            h.Flow.ProcessOwned(MapOnly());
            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly());

            Assert.That(report.Refused, Is.EqualTo(1));
            Assert.That(report.EmptyFetchIgnored, Is.True);
            Assert.That(h.Granter.Revocations, Is.Empty);
            Assert.That(h.Granter.Active, Is.EquivalentTo(new[] { Map }));
        }

        [Test]
        public void FailedRevocation_IsRetriedOnTheNextSync()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapAndClub());
            h.Flow.ProcessOwned(MapOnly());

            h.Granter.FailingRevocations.Add(Club);
            OwnedSyncReport failed = h.Flow.ProcessOwned(MapOnly());
            h.Granter.FailingRevocations.Clear();
            OwnedSyncReport retried = h.Flow.ProcessOwned(MapOnly());

            Assert.That(failed.Revoked, Is.Empty);
            Assert.That(retried.Revoked, Is.EqualTo(new[] { Club }));
        }

        [Test]
        public void ProductThatLeftTheCatalogue_IsNeverRevoked()
        {
            var storage = new InMemoryStringStore();
            new OwnedSetBaseline(storage, Harness.OwnedKey).Save(new[] { Map, "retired_product" });
            var h = new Harness(storage: storage);
            h.Granter.Active.Add("retired_product");

            // The store still reports the retired product as owned, but the flow no longer knows it.
            IReadOnlyList<IStoreOrder> fetched = Orders(Harness.Owned(Map, "tx-map"), Harness.Owned("retired_product", "tx-retired"));
            h.Flow.ProcessOwned(fetched);
            OwnedSyncReport report = h.Flow.ProcessOwned(fetched);

            Assert.That(report.Revoked, Is.Empty);
            Assert.That(report.MissingOnce, Is.EqualTo(0));
            Assert.That(h.Granter.Revocations, Is.Empty);
            Assert.That(h.Granter.Active, Has.Member("retired_product"));
            Assert.That(storage.Read(Harness.OwnedKey), Is.EqualTo(Map + "\n" + "retired_product"));
        }

        [Test]
        public void BaselineThatCannotBeRead_RevokesNothingAndStillRestores()
        {
            var storage = new FlakyStringStore();
            var h = new Harness(storage: storage);
            h.Flow.ProcessOwned(MapAndClub());
            h.Flow.ProcessOwned(MapOnly());
            h.Granter.Active.Remove(Map);

            storage.FailingReads = 2;
            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly());

            Assert.That(report.Granted, Is.EqualTo(1), "the restore itself still works");
            Assert.That(report.Revoked, Is.Empty);
            Assert.That(h.Granter.Revocations, Is.Empty);
            Assert.That(h.Telemetry.Count("diagnostic"), Is.EqualTo(1));
        }

        // ---- lists that are not a complete snapshot ----

        [Test]
        public void PartialList_GrantsButNeverRevokes()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapAndClub());
            h.Granter.Active.Remove(Club);

            // A store SDK can report one order on its own through the fetch callback.
            IReadOnlyList<IStoreOrder> single = Orders(Harness.Owned(Club, "tx-club"));
            OwnedSyncReport first = h.Flow.ProcessOwned(single, null, false);
            OwnedSyncReport second = h.Flow.ProcessOwned(single, null, false);

            Assert.That(first.Granted, Is.EqualTo(1));
            Assert.That(first.MissingOnce, Is.EqualTo(0));
            Assert.That(second.Revoked, Is.Empty);
            Assert.That(h.Granter.Revocations, Is.Empty);
            Assert.That(h.Granter.Active, Is.EquivalentTo(new[] { Map, Club }));
            Assert.That(h.Storage.Read(Harness.OwnedKey), Is.EqualTo(Club + "\n" + Map));
        }

        [Test]
        public void PartialList_AddsToTheBaseline_AndClearsAFirstMiss()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapAndClub());
            h.Flow.ProcessOwned(MapOnly());

            h.Flow.ProcessOwned(Orders(Harness.Owned(Club, "tx-club")), null, false);
            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly());

            Assert.That(report.MissingOnce, Is.EqualTo(1), "the miss before the partial list no longer counts");
            Assert.That(report.Revoked, Is.Empty);
        }

        [Test]
        public void EmptyPartialList_ChangesNothing()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapAndClub());

            OwnedSyncReport report = h.Flow.ProcessOwned(Orders(), null, false);

            Assert.That(report.EmptyFetchIgnored, Is.False);
            Assert.That(h.Granter.Revocations, Is.Empty);
            Assert.That(h.Storage.Read(Harness.OwnedKey), Is.EqualTo(Club + "\n" + Map));
        }

        // ---- orders that are still unfinished in the same fetch ----

        [Test]
        public void ProductWithAGenuineUnfinishedOrder_CountsAsOwned()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapAndClub());

            // The subscription renewed while the app was closed. Until the renewal is confirmed
            // the store lists it as pending, not as owned.
            IReadOnlyList<IStoreOrder> unfinished = Orders(Harness.Owned(Club, "tx-club-renewal"));
            OwnedSyncReport first = h.Flow.ProcessOwned(MapOnly(), unfinished, true);
            OwnedSyncReport second = h.Flow.ProcessOwned(MapOnly(), unfinished, true);

            Assert.That(first.MissingOnce, Is.EqualTo(0));
            Assert.That(second.Revoked, Is.Empty);
            Assert.That(h.Granter.Revocations, Is.Empty);
            Assert.That(h.Granter.Grants, Has.Count.EqualTo(2), "the unfinished order is granted on the pending path, not here");
        }

        [Test]
        public void OnlyDurableBeingUnfinished_IsNotMistakenForAnEmptyFetch()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(Orders(Harness.Owned(Club, "tx-club")));

            OwnedSyncReport report = h.Flow.ProcessOwned(Orders(), Orders(Harness.Owned(Club, "tx-club-renewal")), true);

            Assert.That(report.EmptyFetchIgnored, Is.False);
            Assert.That(h.Telemetry.Count("empty-fetch-ignored"), Is.EqualTo(0));
            Assert.That(h.Storage.Read(Harness.OwnedKey), Is.EqualTo(Club));
        }

        [Test]
        public void ForgedUnfinishedOrder_DoesNotKeepALapsedProductAlive()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapAndClub());
            h.Gate.VerdictByProduct[Club] = ReceiptCheck.Invalid(VerdictReason.SignatureMismatch);

            IReadOnlyList<IStoreOrder> forged = Orders(Harness.Owned(Club, "tx-forged"));
            h.Flow.ProcessOwned(MapOnly(), forged, true);
            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly(), forged, true);

            Assert.That(report.Revoked, Is.EqualTo(new[] { Club }));
        }

        [Test]
        public void UnpaidUnfinishedOrder_DoesNotKeepALapsedProductAlive()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapAndClub());
            h.Gate.VerdictByProduct[Club] = ReceiptCheck.Deferred(VerdictReason.NotPurchased);

            IReadOnlyList<IStoreOrder> unpaid = Orders(Harness.Owned(Club, "tx-unpaid"));
            h.Flow.ProcessOwned(MapOnly(), unpaid, true);
            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly(), unpaid, true);

            Assert.That(report.Revoked, Is.EqualTo(new[] { Club }));
        }

        [Test]
        public void UnfinishedOrderWithoutATransactionId_DoesNotCount()
        {
            var h = new Harness();
            h.Flow.ProcessOwned(MapAndClub());

            IReadOnlyList<IStoreOrder> noTransactionId = Orders(Harness.Owned(Club, string.Empty));
            h.Flow.ProcessOwned(MapOnly(), noTransactionId, true);
            OwnedSyncReport report = h.Flow.ProcessOwned(MapOnly(), noTransactionId, true);

            Assert.That(report.Revoked, Is.EqualTo(new[] { Club }));
        }

        [Test]
        public void UnfinishedConsumable_IsNotTrackedAsOwned()
        {
            var h = new Harness();

            h.Flow.ProcessOwned(MapOnly(), Orders(Harness.Owned(TestCatalog.LanternOil, "tx-oil")), true);

            Assert.That(h.Storage.Read(Harness.OwnedKey), Is.EqualTo(Map));
        }
    }
}
