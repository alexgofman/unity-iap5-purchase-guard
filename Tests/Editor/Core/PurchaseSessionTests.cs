using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Iap5PurchaseGuard.Tests
{
    [TestFixture]
    public class PurchaseSessionTests
    {
        private const string Oil = TestCatalog.LanternOil;
        private const string Map = TestCatalog.MapExpansion;
        private const string Club = TestCatalog.ExplorerClub;

        private static readonly IReadOnlyList<IStoreOrder> None = new IStoreOrder[0];

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

        // ---- connecting ----

        [Test]
        public void AfterConnecting_ProductsComeFirst_ThenPurchasesOnce()
        {
            var h = new SessionHarness(fetchOnConnect: true);

            h.Session.StoreConnected();
            Assert.That(h.Store.Calls, Is.EqualTo(new[] { "fetch-products" }));

            h.Session.ProductsFetched();
            h.Session.ProductsFetched();
            Assert.That(h.Store.Calls, Is.EqualTo(new[] { "fetch-products", "fetch-purchases" }));
        }

        [Test]
        public void EveryReconnection_FetchesPurchasesAgain()
        {
            var h = new SessionHarness(fetchOnConnect: true);

            h.ConnectAndFetchProducts();
            h.ConnectAndFetchProducts();

            Assert.That(h.Store.Count("fetch-purchases"), Is.EqualTo(2));
        }

        [Test]
        public void WithoutFetchOnConnect_PurchasesAreNotFetched()
        {
            var h = new SessionHarness(fetchOnConnect: false);

            h.ConnectAndFetchProducts();

            Assert.That(h.Store.Calls, Is.EqualTo(new[] { "fetch-products" }));
        }

        // ---- complete snapshots and other lists ----

        [Test]
        public void AnswersToRequestedFetches_CanRevoke()
        {
            var h = new SessionHarness();
            h.ConnectAndFetchProducts();
            h.Session.PurchasesFetched(MapAndClub(), None);

            h.ConnectAndFetchProducts();
            h.Session.PurchasesFetched(MapOnly(), None);
            h.ConnectAndFetchProducts();
            h.Session.PurchasesFetched(MapOnly(), None);

            Assert.That(h.Granter.Revocations, Is.EqualTo(new[] { Club }));
            Assert.That(h.Synced, Has.Count.EqualTo(3));
        }

        [Test]
        public void ListsNobodyAskedFor_NeverRevoke()
        {
            var h = new SessionHarness();
            h.ConnectAndFetchProducts();
            h.Session.PurchasesFetched(MapAndClub(), None);

            // The store passes a single order through the fetch callback, several times.
            h.Session.PurchasesFetched(MapOnly(), None);
            h.Session.PurchasesFetched(MapOnly(), None);
            h.Session.PurchasesFetched(MapOnly(), None);

            Assert.That(h.Granter.Revocations, Is.Empty);
            Assert.That(h.Granter.Active, Is.EquivalentTo(new[] { Map, Club }));
        }

        [Test]
        public void ListNobodyAskedFor_IsStillGrantedFrom()
        {
            var h = new SessionHarness();

            h.Session.PurchasesFetched(MapOnly(), None);

            Assert.That(h.Granter.Grants, Is.EqualTo(new[] { Map + "/Restore" }));
        }

        [Test]
        public void OneRequest_CoversOneAnswer()
        {
            var h = new SessionHarness();
            h.ConnectAndFetchProducts();
            h.Session.PurchasesFetched(MapAndClub(), None);

            // One fetch is requested. Its answer counts; the two lists after it do not.
            h.ConnectAndFetchProducts();
            h.Session.PurchasesFetched(MapOnly(), None);
            h.Session.PurchasesFetched(MapOnly(), None);
            h.Session.PurchasesFetched(MapOnly(), None);

            Assert.That(h.Granter.Revocations, Is.Empty);
        }

        [Test]
        public void UnfinishedOrdersInTheAnswer_KeepTheirProductOwned()
        {
            var h = new SessionHarness();
            h.ConnectAndFetchProducts();
            h.Session.PurchasesFetched(MapAndClub(), None);
            IReadOnlyList<IStoreOrder> renewal = Orders(Harness.Owned(Club, "tx-club-renewal"));

            h.ConnectAndFetchProducts();
            h.Session.PurchasesFetched(MapOnly(), renewal);
            h.ConnectAndFetchProducts();
            h.Session.PurchasesFetched(MapOnly(), renewal);

            Assert.That(h.Granter.Revocations, Is.Empty);
        }

        // ---- restore ----

        [Test]
        public void Restore_AsksTheStore_AndReportsWhatWasRestored()
        {
            var h = new SessionHarness(fetchOnConnect: false, processOwnedOnEveryFetch: false);

            h.Session.RestorePurchases();
            h.Session.PurchasesFetched(MapOnly(), None);

            Assert.That(h.Store.Calls, Is.EqualTo(new[] { "restore-transactions" }));
            Assert.That(h.Synced, Has.Count.EqualTo(1));
            Assert.That(h.Synced[0].Granted, Is.EqualTo(1));
            Assert.That(h.RestoreFailures, Is.Empty);
        }

        [Test]
        public void RestoreOnlyMode_IgnoresOwnedOrdersUntilThePlayerAsks()
        {
            var h = new SessionHarness(fetchOnConnect: true, processOwnedOnEveryFetch: false);

            h.ConnectAndFetchProducts();
            h.Session.PurchasesFetched(MapOnly(), None);

            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Synced, Is.Empty);

            h.Session.RestorePurchases();
            h.Session.PurchasesFetched(MapOnly(), None);

            Assert.That(h.Granter.Grants, Is.EqualTo(new[] { Map + "/Restore" }));
        }

        [Test]
        public void RestoreOnlyMode_StillHandlesPendingOrders()
        {
            var h = new SessionHarness(fetchOnConnect: true, processOwnedOnEveryFetch: false);

            h.Session.PendingOrderDelivered(SessionHarness.Pending("tx-1", Oil));

            Assert.That(h.Granter.Grants, Is.EqualTo(new[] { Oil + "/Purchase" }));
            Assert.That(h.Store.Count("confirm:tx-1"), Is.EqualTo(1));
        }

        [Test]
        public void Restore_WhenTheStoreIsNotConnected_FailsAndAsksForAConnection()
        {
            var h = new SessionHarness();
            h.Store.IsConnected = false;

            h.Session.RestorePurchases();

            Assert.That(h.Store.Calls, Is.EqualTo(new[] { "connect" }));
            Assert.That(h.RestoreFailures, Has.Count.EqualTo(1));
        }

        [Test]
        public void RestoreRejectedByTheStore_FailsOnce()
        {
            var h = new SessionHarness(fetchOnConnect: false, processOwnedOnEveryFetch: false);

            h.Session.RestorePurchases();
            h.Session.RestoreRejected("not signed in");
            h.Session.RestoreRejected("not signed in");

            Assert.That(h.RestoreFailures, Is.EqualTo(new[] { "not signed in" }));

            // The restore is over. A list that turns up later is not its answer.
            h.Session.PurchasesFetched(MapOnly(), None);
            Assert.That(h.Granter.Grants, Is.Empty);
        }

        [Test]
        public void FetchFailureDuringARestore_FailsTheRestore()
        {
            var h = new SessionHarness(fetchOnConnect: false, processOwnedOnEveryFetch: false);

            h.Session.RestorePurchases();
            h.Session.PurchasesFetchFailed("StoreNotConnected");

            Assert.That(h.RestoreFailures, Is.EqualTo(new[] { "StoreNotConnected" }));
            Assert.That(h.Synced, Is.Empty);
        }

        [Test]
        public void FetchFailureWithoutARestore_OnlyWarns()
        {
            var h = new SessionHarness();
            h.ConnectAndFetchProducts();

            h.Session.PurchasesFetchFailed("StoreNotConnected");

            Assert.That(h.RestoreFailures, Is.Empty);
            Assert.That(h.Granter.Revocations, Is.Empty);
            Assert.That(h.Telemetry.Count("diagnostic"), Is.EqualTo(1));
        }

        [Test]
        public void FailedFetch_DoesNotLeaveTheNextListLookingLikeItsAnswer()
        {
            var h = new SessionHarness();
            h.ConnectAndFetchProducts();
            h.Session.PurchasesFetched(MapAndClub(), None);

            h.ConnectAndFetchProducts();
            h.Session.PurchasesFetchFailed("StoreNotConnected");
            h.Session.PurchasesFetched(MapOnly(), None);

            Assert.That(h.Synced[h.Synced.Count - 1].MissingOnce, Is.EqualTo(0), "the list is not counted as a complete answer");

            h.Session.PurchasesFetched(MapOnly(), None);

            Assert.That(h.Granter.Revocations, Is.Empty);
        }

        // ---- buying ----

        [Test]
        public void Buy_StartsThePurchase_AndTheOrderIsGrantedWhenItArrives()
        {
            var h = new SessionHarness();

            bool started = h.Session.Buy(Oil);
            h.Session.PendingOrderDelivered(SessionHarness.Pending("tx-1", Oil));

            Assert.That(started, Is.True);
            Assert.That(h.Store.Calls, Is.EqualTo(new[] { "buy:" + Oil, "confirm:tx-1" }));
            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "succeeded:" + Oil }));
        }

        [Test]
        public void Buy_ProductOutsideTheCatalogue_NeverReachesTheStore()
        {
            var h = new SessionHarness();

            bool started = h.Session.Buy("retired_product");

            Assert.That(started, Is.False);
            Assert.That(h.Store.Calls, Is.Empty);
            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "failed:retired_product:ProductUnavailable" }));
        }

        [Test]
        public void Buy_WhenPurchasesAreBlocked_NeverReachesTheStore()
        {
            var h = new SessionHarness(blockPurchases: true);

            bool started = h.Session.Buy(Oil);

            Assert.That(started, Is.False);
            Assert.That(h.Store.Calls, Is.Empty);
            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "failed:" + Oil + ":UnofficialInstall" }));
            Assert.That(h.Telemetry.Events, Is.EqualTo(new[] { "purchase-failed:" + Oil + ":UnofficialInstall" }));
        }

        [Test]
        public void Buy_WhenTheStoreIsNotConnected_AsksForAConnection()
        {
            var h = new SessionHarness();
            h.Store.IsConnected = false;

            bool started = h.Session.Buy(Oil);

            Assert.That(started, Is.False);
            Assert.That(h.Store.Calls, Is.EqualTo(new[] { "connect" }));
            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "failed:" + Oil + ":StoreNotReady" }));
        }

        [Test]
        public void Buy_WhenTheStoreCannotSellTheProduct_Fails()
        {
            var h = new SessionHarness();
            h.Store.CanSell = false;

            bool started = h.Session.Buy(Oil);

            Assert.That(started, Is.False);
            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "failed:" + Oil + ":ProductUnavailable" }));
        }

        [Test]
        public void StartedPurchaseAnsweredByAnOrderAlreadyGranted_EndsAsAlreadyOwned()
        {
            var h = new SessionHarness();
            FakeOrder order = SessionHarness.Pending("tx-1", Oil);
            h.Session.PendingOrderDelivered(order);
            h.Ui.Messages.Clear();

            // The confirmation never took effect. The player buys again and the store answers with
            // the order it still holds.
            h.Session.Buy(Oil);
            h.Session.PendingOrderDelivered(order);

            Assert.That(h.Granter.Grants, Has.Count.EqualTo(1));
            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "failed:" + Oil + ":AlreadyOwned" }));
        }

        [Test]
        public void RedeliveryThatAnswersNoPurchase_IsSilent()
        {
            var h = new SessionHarness();
            FakeOrder order = SessionHarness.Pending("tx-1", Oil);
            h.Session.PendingOrderDelivered(order);
            h.Ui.Messages.Clear();

            h.Session.PendingOrderDelivered(order);

            Assert.That(h.Ui.Messages, Is.Empty);
            Assert.That(h.Store.Count("confirm:tx-1"), Is.EqualTo(2));
        }

        [Test]
        public void OrderForAnotherProduct_DoesNotAnswerThePurchaseInFlight()
        {
            var h = new SessionHarness();
            FakeOrder oldMapOrder = SessionHarness.Pending("tx-map", Map);
            h.Session.PendingOrderDelivered(oldMapOrder);
            h.Ui.Messages.Clear();

            h.Session.Buy(Oil);
            h.Session.PendingOrderDelivered(oldMapOrder);
            Assert.That(h.Ui.Messages, Is.Empty, "the redelivery of another order says nothing about the purchase in flight");

            h.Session.PendingOrderDelivered(SessionHarness.Pending("tx-oil", Oil));
            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "succeeded:" + Oil }));
        }

        [Test]
        public void FailedPurchase_TellsThePlayerAndTheTelemetry()
        {
            var h = new SessionHarness();
            h.Session.Buy(Oil);

            h.Session.PurchaseFailed(Oil, PurchaseFailureKind.Cancelled, "cancelled by the player");

            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "failed:" + Oil + ":Cancelled" }));
            Assert.That(h.Telemetry.Events, Is.EqualTo(new[] { "purchase-failed:" + Oil + ":Cancelled" }));
        }

        [Test]
        public void DeferredPurchase_TellsThePlayerItIsPending()
        {
            var h = new SessionHarness();
            h.Session.Buy(Map);

            h.Session.PurchaseDeferred(Map);

            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "pending:" + Map }));
            Assert.That(h.Granter.Grants, Is.Empty);
        }

        // ---- confirmations ----

        [Test]
        public void FailedConfirmation_IsSentAgainAfterTheNextConnection()
        {
            var h = new SessionHarness(fetchOnConnect: false);
            h.Session.PendingOrderDelivered(SessionHarness.Pending("tx-1", Oil));
            h.Session.ConfirmFailed("tx-1", "StoreNotConnected");

            h.Session.StoreConnected();

            Assert.That(h.Store.Count("confirm:tx-1"), Is.EqualTo(2));
            Assert.That(h.Granter.Grants, Has.Count.EqualTo(1));
        }

        [Test]
        public void SucceededConfirmation_IsNotSentAgain()
        {
            var h = new SessionHarness(fetchOnConnect: false);
            h.Session.PendingOrderDelivered(SessionHarness.Pending("tx-1", Oil));
            h.Session.ConfirmSucceeded("tx-1");

            h.Session.StoreConnected();

            Assert.That(h.Store.Count("confirm:tx-1"), Is.EqualTo(1));
        }

        [Test]
        public void AlreadyOwnedAnswer_SendsOutstandingConfirmationsAgain_AndFetches()
        {
            var h = new SessionHarness(fetchOnConnect: true);
            h.Session.PendingOrderDelivered(SessionHarness.Pending("tx-1", Oil));
            h.Session.ConfirmFailed("tx-1", "ServiceUnavailable");
            h.Store.Calls.Clear();

            // The consumable was never consumed, so the store refuses to sell it again.
            h.Session.Buy(Oil);
            h.Session.PurchaseFailed(Oil, PurchaseFailureKind.AlreadyOwned, "the store says it is already owned");

            Assert.That(h.Store.Calls, Is.EqualTo(new[] { "buy:" + Oil, "confirm:tx-1", "fetch-purchases" }));
        }

        [Test]
        public void AlreadyOwnedAnswer_WithoutFetchOnConnect_DoesNotFetch()
        {
            var h = new SessionHarness(fetchOnConnect: false, processOwnedOnEveryFetch: false);

            h.Session.PurchaseFailed(Oil, PurchaseFailureKind.AlreadyOwned, "the store says it is already owned");

            Assert.That(h.Store.Calls, Is.Empty);
        }

        [Test]
        public void ConfirmThatThrows_KeepsTheGrant_AndIsSentAgain()
        {
            var h = new SessionHarness(fetchOnConnect: false);
            h.Store.ThrowOnConfirm = true;
            h.Session.PendingOrderDelivered(SessionHarness.Pending("tx-1", Oil));

            h.Store.ThrowOnConfirm = false;
            h.Session.StoreConnected();

            Assert.That(h.Granter.Grants, Has.Count.EqualTo(1));
            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "succeeded:" + Oil }));
            Assert.That(h.Store.Count("confirm:tx-1"), Is.EqualTo(2));
        }

        // ---- things going wrong around the flow ----

        [Test]
        public void StorageFailureWhileAPurchaseIsInFlight_TellsThePaywall()
        {
            var storage = new FlakyStringStore();
            var h = new SessionHarness(storage: storage);
            h.Session.Buy(Oil);

            storage.FailingReads = 1;
            h.Session.PendingOrderDelivered(SessionHarness.Pending("tx-1", Oil));

            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Store.Count("confirm:tx-1"), Is.EqualTo(0));
            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "failed:" + Oil + ":Other" }));
        }

        [Test]
        public void StorageFailureWithNoPurchaseInFlight_IsSilent()
        {
            var storage = new FlakyStringStore();
            var h = new SessionHarness(storage: storage);

            storage.FailingReads = 1;
            h.Session.PendingOrderDelivered(SessionHarness.Pending("tx-1", Oil));

            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Ui.Messages, Is.Empty);
            Assert.That(h.Telemetry.Count("diagnostic"), Is.EqualTo(1));
        }

        [Test]
        public void UnreadablePendingOrder_ReleasesAWaitingPaywall()
        {
            var h = new SessionHarness();
            h.Session.Buy(Oil);

            h.Session.PendingOrderUnreadable("the cart could not be read");
            h.Session.PendingOrderUnreadable("the cart could not be read");

            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "failed:" + Oil + ":Other" }));
        }

        [Test]
        public void ThrowingSubscriber_DoesNotEscapeIntoTheStoreCallback()
        {
            var h = new SessionHarness();
            h.Session.OwnedPurchasesSynced += report => throw new InvalidOperationException("the shop screen is gone");

            Assert.DoesNotThrow(() => h.Session.PurchasesFetched(MapOnly(), None));
            Assert.That(h.Granter.Grants, Has.Count.EqualTo(1));
        }

        [Test]
        public void Reset_ForgetsWhatWasInFlight()
        {
            var h = new SessionHarness(fetchOnConnect: false);
            h.Session.PendingOrderDelivered(SessionHarness.Pending("tx-1", Oil));
            h.Session.Buy(Oil);
            h.Session.RestorePurchases();

            h.Session.Reset();
            h.Session.StoreConnected();
            h.Session.RestoreRejected("late answer");

            Assert.That(h.Store.Count("confirm:tx-1"), Is.EqualTo(1));
            Assert.That(h.RestoreFailures, Is.Empty);
        }

        [Test]
        public void NullPendingOrder_IsACallerError()
        {
            var h = new SessionHarness();

            Assert.Throws<ArgumentNullException>(() => h.Session.PendingOrderDelivered(null));
        }
    }
}
