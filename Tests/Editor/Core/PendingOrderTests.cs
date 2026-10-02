using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Iap5PurchaseGuard.Tests
{
    [TestFixture]
    public class PendingOrderTests
    {
        private const string Tx = "tx-0001";

        [Test]
        public void ValidOrder_IsGrantedThenConfirmedThenAnnounced_InThatOrder()
        {
            var h = new Harness();
            h.Store.Purchase(Tx, TestCatalog.LanternOil);

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Granted }));
            Assert.That(h.Journal, Is.EqualTo(new[]
            {
                "gate",
                "grant:" + TestCatalog.LanternOil,
                "save:" + Harness.LedgerKey,
                "confirm:" + Tx,
                "ui:succeeded:" + TestCatalog.LanternOil,
                "telemetry:granted:" + TestCatalog.LanternOil + ":" + Tx
            }));
            Assert.That(h.Store.Unconfirmed, Is.Empty);
            Assert.That(h.Granter.GrantedTransactions, Is.EqualTo(new[] { Tx }), "the game is told which transaction it is granting");
        }

        [Test]
        public void LedgerThatCannotBeRead_LeavesTheOrderUnconfirmedAndIsRetried()
        {
            var storage = new FlakyStringStore();
            var h = new Harness(storage: storage);
            h.Store.Purchase(Tx, TestCatalog.LanternOil);

            // Without the ledger the flow cannot tell a new order from a redelivery, so it does nothing.
            storage.FailingReads = 1;
            Assert.Throws<InvalidOperationException>(() => h.Store.Deliver(h.Flow));
            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Store.ConfirmCalls, Is.EqualTo(0));

            Assert.That(h.Store.Deliver(h.Flow), Is.EqualTo(new[] { PendingOutcome.Granted }));
            Assert.That(h.Granter.Grants, Has.Count.EqualTo(1));
        }

        [Test]
        public void RedeliveredTransaction_IsConfirmedButNotGrantedAgain()
        {
            var h = new Harness();
            h.Store.LoseConfirmations = true;
            h.Store.Purchase(Tx, TestCatalog.LanternOil);

            Assert.That(h.Store.Deliver(h.Flow), Is.EqualTo(new[] { PendingOutcome.Granted }));
            Assert.That(h.Store.Unconfirmed, Has.Count.EqualTo(1), "the confirmation was lost, so the store still holds the order");

            h.Store.LoseConfirmations = false;
            Assert.That(h.Store.Deliver(h.Flow), Is.EqualTo(new[] { PendingOutcome.AlreadyGranted }));

            Assert.That(h.Granter.Grants, Is.EqualTo(new[] { TestCatalog.LanternOil + "/Purchase" }));
            Assert.That(h.Store.ConfirmCalls, Is.EqualTo(2));
            Assert.That(h.Store.Unconfirmed, Is.Empty);
            Assert.That(h.Telemetry.Count("granted:"), Is.EqualTo(1), "a sale is reported once per transaction");
            Assert.That(h.Ui.Messages, Has.Count.EqualTo(1), "the redelivery is silent");
        }

        [Test]
        public void RedeliveryAfterARelaunch_IsConfirmedButNotGrantedAgain()
        {
            var h = new Harness();
            h.Store.LoseConfirmations = true;
            h.Store.Purchase(Tx, TestCatalog.LanternOil);
            h.Store.Deliver(h.Flow);

            h.Relaunch();
            h.Store.LoseConfirmations = false;
            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.AlreadyGranted }));
            Assert.That(h.Granter.Grants, Has.Count.EqualTo(1));
            Assert.That(h.Store.Unconfirmed, Is.Empty);
        }

        [Test]
        public void Redelivery_IsDedupedBeforeTheTrustGate()
        {
            var h = new Harness();
            h.Store.LoseConfirmations = true;
            h.Store.Purchase(Tx, TestCatalog.LanternOil);
            h.Store.Deliver(h.Flow);

            // Even a gate that would now refuse the order is not consulted: it was already judged.
            h.Gate.Verdict = ReceiptCheck.Invalid(VerdictReason.SignatureMismatch);
            h.Store.LoseConfirmations = false;
            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.AlreadyGranted }));
            Assert.That(h.Gate.Calls, Is.EqualTo(1));
            Assert.That(h.Telemetry.Count("refused:"), Is.EqualTo(0));
            Assert.That(h.Store.Unconfirmed, Is.Empty);
        }

        [Test]
        public void InvalidVerdict_IsNeitherGrantedNorConfirmed()
        {
            var h = new Harness();
            h.Gate.Verdict = ReceiptCheck.Invalid(VerdictReason.SignatureMismatch);
            h.Store.Purchase(Tx, TestCatalog.MapExpansion);

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Refused }));
            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Store.ConfirmCalls, Is.EqualTo(0));
            Assert.That(h.Store.Unconfirmed, Has.Count.EqualTo(1));
            Assert.That(h.Storage.Read(Harness.LedgerKey), Is.Empty);
            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "refused:" + TestCatalog.MapExpansion + ":SignatureMismatch" }));
            Assert.That(h.Telemetry.Events, Is.EqualTo(new[] { "refused:" + TestCatalog.MapExpansion + ":SignatureMismatch:Purchase" }));
        }

        [Test]
        public void InvalidVerdict_ReportsNoSale()
        {
            var h = new Harness();
            h.Gate.Verdict = ReceiptCheck.Invalid(VerdictReason.WrongProduct);
            h.Store.Purchase(Tx, TestCatalog.MapExpansion);

            h.Store.Deliver(h.Flow);

            Assert.That(h.Telemetry.Count("granted:"), Is.EqualTo(0));
        }

        [Test]
        public void DeferredVerdict_IsHeldUntilTheOrderIsPaid()
        {
            var h = new Harness();
            h.Gate.Verdict = ReceiptCheck.Deferred(VerdictReason.NotPurchased);
            h.Store.Purchase(Tx, TestCatalog.MapExpansion);

            Assert.That(h.Store.Deliver(h.Flow), Is.EqualTo(new[] { PendingOutcome.Waiting }));
            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Store.ConfirmCalls, Is.EqualTo(0));
            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "pending:" + TestCatalog.MapExpansion }));
            Assert.That(h.Telemetry.Events, Is.Empty, "a payment that is still pending is not a refusal");

            h.Gate.Verdict = ReceiptCheck.Valid(VerdictReason.SignatureVerified);
            Assert.That(h.Store.Deliver(h.Flow), Is.EqualTo(new[] { PendingOutcome.Granted }));
            Assert.That(h.Granter.Grants, Has.Count.EqualTo(1));
            Assert.That(h.Store.Unconfirmed, Is.Empty);
        }

        [Test]
        public void UnavailableVerdict_WithFailOpen_GrantsAndRaisesTheAlarmOncePerSession()
        {
            var h = new Harness(UnavailablePolicy.FailOpen);
            h.Gate.Verdict = ReceiptCheck.Unavailable(VerdictReason.KeyNotConfigured);
            h.Store.Purchase("tx-a", TestCatalog.LanternOil);
            h.Store.Purchase("tx-b", TestCatalog.LanternOil);

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Granted, PendingOutcome.Granted }));
            Assert.That(h.Granter.Grants, Has.Count.EqualTo(2));
            Assert.That(h.Store.Unconfirmed, Is.Empty);
            Assert.That(h.Telemetry.Count("unavailable:KeyNotConfigured"), Is.EqualTo(1));
        }

        [Test]
        public void UnavailableVerdict_WithFailClosed_IsNeitherGrantedNorConfirmed()
        {
            var h = new Harness(UnavailablePolicy.FailClosed);
            h.Gate.Verdict = ReceiptCheck.Unavailable(VerdictReason.KeyNotConfigured);
            h.Store.Purchase(Tx, TestCatalog.LanternOil);

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Refused }));
            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Store.ConfirmCalls, Is.EqualTo(0));
            Assert.That(h.Telemetry.Count("unavailable:KeyNotConfigured"), Is.EqualTo(1));
        }

        [Test]
        public void ThrowingGrant_LeavesTheOrderUnconfirmed()
        {
            var h = new Harness();
            h.Granter.FailingGrants = 1;
            h.Store.Purchase(Tx, TestCatalog.MapExpansion);

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.GrantFailed }));
            Assert.That(h.Store.ConfirmCalls, Is.EqualTo(0));
            Assert.That(h.Store.Unconfirmed, Has.Count.EqualTo(1));
            Assert.That(h.Storage.Read(Harness.LedgerKey), Is.Empty, "a failed grant must not be remembered as granted");
            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "failed:" + TestCatalog.MapExpansion + ":GrantFailed" }));
            Assert.That(h.Telemetry.Events, Is.EqualTo(new[] { "grant-failed:" + TestCatalog.MapExpansion + ":Purchase" }));
        }

        [Test]
        public void ThrowingGrant_IsRetriedOnRedelivery()
        {
            var h = new Harness();
            h.Granter.FailingGrants = 1;
            h.Store.Purchase(Tx, TestCatalog.MapExpansion);
            h.Store.Deliver(h.Flow);

            h.Relaunch();
            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Granted }));
            Assert.That(h.Granter.Grants, Is.EqualTo(new[] { TestCatalog.MapExpansion + "/Purchase" }));
            Assert.That(h.Store.ConfirmCalls, Is.EqualTo(1));
            Assert.That(h.Store.Unconfirmed, Is.Empty);
        }

        [Test]
        public void OrderWithoutAProductId_IsNeitherGrantedNorConfirmed()
        {
            var h = new Harness();
            h.Store.Hold(new FakeOrder { TransactionId = Tx });

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Unidentified }));
            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Store.ConfirmCalls, Is.EqualTo(0), "confirming would end the transaction with nothing granted");
            Assert.That(h.Store.Unconfirmed, Has.Count.EqualTo(1));
            Assert.That(h.Gate.Calls, Is.EqualTo(0));
            Assert.That(h.Telemetry.Events, Is.EqualTo(new[] { "refused::ProductUnknown:Purchase" }));
        }

        [Test]
        public void OrderForAProductOutsideTheCatalogue_IsNeitherGrantedNorConfirmed()
        {
            var h = new Harness();
            h.Store.Purchase(Tx, "retired_product");

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Unidentified }));
            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Store.ConfirmCalls, Is.EqualTo(0));
        }

        [Test]
        public void OrderWithoutATransactionId_IsRefusedWithoutAskingTheGate()
        {
            var h = new Harness();
            h.Store.Purchase(string.Empty, TestCatalog.LanternOil);

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Refused }));
            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Store.ConfirmCalls, Is.EqualTo(0));
            Assert.That(h.Gate.Calls, Is.EqualTo(0));
            Assert.That(h.Telemetry.Events, Is.EqualTo(new[] { "refused:" + TestCatalog.LanternOil + ":TransactionIdMissing:Purchase" }));
        }

        [Test]
        public void TestStoreOrder_IsRefusedWhenTheCheckIsOn()
        {
            var h = new Harness(rejectTestStoreOrders: true);
            FakeOrder order = h.Store.Purchase(Tx, TestCatalog.LanternOil);
            order.StoreName = PurchaseFlowOptions.SdkTestStoreName;

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Refused }));
            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Store.ConfirmCalls, Is.EqualTo(0));
            Assert.That(h.Gate.Calls, Is.EqualTo(0));
            Assert.That(h.Telemetry.Events, Is.EqualTo(new[] { "refused:" + TestCatalog.LanternOil + ":TestStoreReceipt:Purchase" }));
        }

        [Test]
        public void TestStoreOrder_IsGrantedWhenTheCheckIsOff()
        {
            var h = new Harness(rejectTestStoreOrders: false);
            FakeOrder order = h.Store.Purchase(Tx, TestCatalog.LanternOil);
            order.StoreName = PurchaseFlowOptions.SdkTestStoreName;

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Granted }));
            Assert.That(h.Store.Unconfirmed, Is.Empty);
        }

        [Test]
        public void ThrowingGate_IsARefusal()
        {
            var h = new Harness();
            h.Gate.Failure = new FormatException("unexpected receipt shape");
            h.Store.Purchase(Tx, TestCatalog.LanternOil);

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Refused }));
            Assert.That(h.Granter.Grants, Is.Empty);
            Assert.That(h.Store.ConfirmCalls, Is.EqualTo(0));
            Assert.That(h.Telemetry.Count("refused:" + TestCatalog.LanternOil + ":GateFailed"), Is.EqualTo(1));
        }

        [Test]
        public void ThrowingUiAndTelemetry_DoNotUndoAConfirmedOrder()
        {
            var h = new Harness();
            h.Ui.Throws = true;
            h.Telemetry.Throws = true;
            h.Store.Purchase(Tx, TestCatalog.LanternOil);

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Granted }));
            Assert.That(h.Granter.Grants, Has.Count.EqualTo(1));
            Assert.That(h.Store.Unconfirmed, Is.Empty);
            Assert.That(h.Storage.Read(Harness.LedgerKey), Is.EqualTo(Tx));
        }

        [Test]
        public void ThrowingConfirm_KeepsTheGrantAndIsRepairedByTheNextDelivery()
        {
            var h = new Harness();
            h.Store.ThrowOnConfirm = true;
            h.Store.Purchase(Tx, TestCatalog.LanternOil);

            Assert.That(h.Store.Deliver(h.Flow), Is.EqualTo(new[] { PendingOutcome.Granted }));
            Assert.That(h.Ui.Messages, Is.EqualTo(new[] { "succeeded:" + TestCatalog.LanternOil }), "the player received the product");

            h.Store.ThrowOnConfirm = false;
            Assert.That(h.Store.Deliver(h.Flow), Is.EqualTo(new[] { PendingOutcome.AlreadyGranted }));
            Assert.That(h.Granter.Grants, Has.Count.EqualTo(1));
            Assert.That(h.Store.Unconfirmed, Is.Empty);
        }

        [Test]
        public void LedgerThatCannotBeSaved_StillConfirmsTheOrder()
        {
            var h = new Harness(storage: new ReadOnlyStringStore());
            h.Store.Purchase(Tx, TestCatalog.LanternOil);

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Granted }));
            Assert.That(h.Store.Unconfirmed, Is.Empty, "an unconfirmed order could only be granted a second time");
            Assert.That(h.Telemetry.Count("diagnostic"), Is.EqualTo(1));
        }

        [Test]
        public void TwoTransactionsForTheSameConsumable_AreBothGranted()
        {
            var h = new Harness();
            h.Store.Purchase("tx-a", TestCatalog.LanternOil);
            h.Store.Purchase("tx-b", TestCatalog.LanternOil);

            List<PendingOutcome> outcomes = h.Store.Deliver(h.Flow);

            Assert.That(outcomes, Is.EqualTo(new[] { PendingOutcome.Granted, PendingOutcome.Granted }));
            Assert.That(h.Granter.Grants, Has.Count.EqualTo(2));
        }

        [Test]
        public void NullOrder_IsRejectedAsACallerError()
        {
            var h = new Harness();

            Assert.Throws<ArgumentNullException>(() => h.Flow.ProcessPending(null));
        }
    }
}
