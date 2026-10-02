using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Iap5PurchaseGuard.Tests
{
    /// <summary>
    /// Drives the flow with seeded random sequences of purchases, deliveries, lost and failing
    /// confirmations, failing grants and restarts, and checks after every delivery that the
    /// promises of the pipeline still hold. The seeds are fixed, so a failure can be replayed.
    /// </summary>
    [TestFixture]
    public class DeliveryInvariantTests
    {
        private const int Seeds = 300;
        private const int StepsPerSeed = 60;

        private enum Truth
        {
            Genuine,
            Forged,
            Unpaid,
            Unidentified
        }

        private sealed class TruthGate : IReceiptGate
        {
            public Dictionary<string, Truth> ByTransaction { get; } = new Dictionary<string, Truth>();

            public ReceiptCheck Check(IStoreOrder order)
            {
                switch (ByTransaction[order.TransactionId])
                {
                    case Truth.Forged:
                        return ReceiptCheck.Invalid(VerdictReason.SignatureMismatch);
                    case Truth.Unpaid:
                        return ReceiptCheck.Deferred(VerdictReason.NotPurchased);
                    default:
                        return ReceiptCheck.Valid(VerdictReason.SignatureVerified);
                }
            }
        }

        private sealed class World
        {
            private readonly List<string> _journal = new List<string>();
            private readonly InMemoryStringStore _storage = new InMemoryStringStore();

            public World()
            {
                Store = new FakeStore(_journal);
                Granter = new RecordingGranter(_journal);
                Telemetry = new RecordingTelemetry(_journal);
                Restart();
            }

            public FakeStore Store { get; }

            public TruthGate Gate { get; } = new TruthGate();

            public RecordingGranter Granter { get; }

            public RecordingTelemetry Telemetry { get; }

            public Dictionary<string, int> GrantsByTransaction { get; } = new Dictionary<string, int>();

            public PurchaseFlow Flow { get; private set; }

            /// <summary>A new flow and a new ledger object over the same storage.</summary>
            public void Restart()
            {
                Flow = new PurchaseFlow(
                    TestCatalog.Create(),
                    new TransactionLedger(_storage, Harness.LedgerKey),
                    new OwnedSetBaseline(_storage, Harness.OwnedKey),
                    Gate,
                    new VerdictPolicy(UnavailablePolicy.FailClosed),
                    Granter,
                    Store,
                    null,
                    Telemetry);
            }
        }

        private static readonly string[] Products =
        {
            TestCatalog.LanternOil,
            TestCatalog.MapExpansion,
            TestCatalog.ExplorerClub
        };

        [Test]
        public void RandomSequences_KeepEveryPromise()
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                RunSequence(seed);
            }
        }

        private static void RunSequence(int seed)
        {
            var random = new Random(seed);
            var world = new World();
            var orders = new List<FakeOrder>();

            for (int step = 0; step < StepsPerSeed; step++)
            {
                string at = "seed " + seed + ", step " + step;
                switch (random.Next(11))
                {
                    case 0:
                    case 1:
                    case 2:
                        Buy(world, orders, random);
                        break;
                    case 3:
                    case 4:
                    case 5:
                        DeliverAll(world, at);
                        break;
                    case 6:
                        world.Store.LoseConfirmations = !world.Store.LoseConfirmations;
                        break;
                    case 7:
                        world.Store.ThrowOnConfirm = !world.Store.ThrowOnConfirm;
                        break;
                    case 8:
                        world.Granter.FailingGrants = random.Next(3);
                        break;
                    case 9:
                        world.Restart();
                        break;
                    default:
                        CompleteOnePayment(world, orders, random);
                        break;
                }
            }

            // Let everything settle: the store works, the game's grant works, and the store gets
            // enough deliveries to finish what was left half done.
            world.Store.LoseConfirmations = false;
            world.Store.ThrowOnConfirm = false;
            world.Granter.FailingGrants = 0;
            DeliverAll(world, "seed " + seed + ", settling");
            DeliverAll(world, "seed " + seed + ", settling again");

            foreach (FakeOrder order in orders)
            {
                string at = "seed " + seed + ", order " + order.TransactionId;
                if (world.Gate.ByTransaction[order.TransactionId] == Truth.Genuine)
                {
                    Assert.That(world.GrantsByTransaction[order.TransactionId], Is.EqualTo(1), "a genuine paid order ends up granted exactly once (" + at + ")");
                    Assert.That(order.Confirmed, Is.True, "a genuine paid order ends up confirmed (" + at + ")");
                }
                else
                {
                    Assert.That(order.Confirmed, Is.False, "a forged, unpaid or unidentified order is never confirmed (" + at + ")");
                }
            }
        }

        private static void Buy(World world, List<FakeOrder> orders, Random random)
        {
            string transactionId = "tx-" + orders.Count.ToString("D4");
            int roll = random.Next(10);
            Truth truth = roll < 6 ? Truth.Genuine : roll < 8 ? Truth.Forged : roll < 9 ? Truth.Unpaid : Truth.Unidentified;

            // An unidentified order names a product the catalogue does not have, or no product at all.
            string productId = truth == Truth.Unidentified
                ? (random.Next(2) == 0 ? "retired_product" : string.Empty)
                : Products[random.Next(Products.Length)];

            world.Gate.ByTransaction[transactionId] = truth;
            world.GrantsByTransaction[transactionId] = 0;
            orders.Add(world.Store.Purchase(transactionId, productId));
        }

        private static void CompleteOnePayment(World world, List<FakeOrder> orders, Random random)
        {
            var unpaid = new List<FakeOrder>();
            foreach (FakeOrder order in orders)
            {
                if (world.Gate.ByTransaction[order.TransactionId] == Truth.Unpaid)
                {
                    unpaid.Add(order);
                }
            }

            if (unpaid.Count > 0)
            {
                world.Gate.ByTransaction[unpaid[random.Next(unpaid.Count)].TransactionId] = Truth.Genuine;
            }
        }

        private static void DeliverAll(World world, string at)
        {
            foreach (FakeOrder order in new List<FakeOrder>(world.Store.Unconfirmed))
            {
                string transactionId = order.TransactionId;
                int grantsBefore = world.Granter.Grants.Count;

                world.Flow.ProcessPending(order);

                world.GrantsByTransaction[transactionId] += world.Granter.Grants.Count - grantsBefore;
                int grants = world.GrantsByTransaction[transactionId];
                Truth truth = world.Gate.ByTransaction[transactionId];
                string where = " (" + at + ", " + transactionId + ")";

                Assert.That(grants, Is.LessThanOrEqualTo(1), "a transaction is never granted twice" + where);
                Assert.That(world.Telemetry.Count("granted:" + order.ProductId + ":" + transactionId), Is.LessThanOrEqualTo(1), "a sale is never reported twice" + where);

                if (order.Confirmed)
                {
                    Assert.That(grants, Is.EqualTo(1), "an order is never confirmed without having been granted" + where);
                }

                if (truth != Truth.Genuine)
                {
                    Assert.That(grants, Is.EqualTo(0), "a forged, unpaid or unidentified order is never granted" + where);
                    Assert.That(order.Confirmed, Is.False, "a forged, unpaid or unidentified order is never confirmed" + where);
                }
            }
        }
    }
}
