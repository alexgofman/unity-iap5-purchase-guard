using System;
using NUnit.Framework;

namespace Iap5PurchaseGuard.Tests
{
    [TestFixture]
    public class TransactionLedgerTests
    {
        private const string Key = "test.ledger";

        [Test]
        public void RemembersWhatWasAdded()
        {
            var ledger = new TransactionLedger(new InMemoryStringStore(), Key);

            Assert.That(ledger.Contains("tx-1"), Is.False);
            Assert.That(ledger.Add("tx-1"), Is.True);
            Assert.That(ledger.Contains("tx-1"), Is.True);
            Assert.That(ledger.Count, Is.EqualTo(1));
        }

        [Test]
        public void AddingTheSameIdTwice_KeepsOneEntry()
        {
            var ledger = new TransactionLedger(new InMemoryStringStore(), Key);

            ledger.Add("tx-1");

            Assert.That(ledger.Add("tx-1"), Is.False);
            Assert.That(ledger.Count, Is.EqualTo(1));
        }

        [Test]
        public void EmptyIds_AreNeverStoredOrFound()
        {
            var ledger = new TransactionLedger(new InMemoryStringStore(), Key);

            Assert.That(ledger.Add(null), Is.False);
            Assert.That(ledger.Add(string.Empty), Is.False);
            Assert.That(ledger.Add("   "), Is.False);
            Assert.That(ledger.Contains(null), Is.False);
            Assert.That(ledger.Contains(string.Empty), Is.False);
            Assert.That(ledger.Count, Is.EqualTo(0));
        }

        [Test]
        public void SurvivesARestart()
        {
            var storage = new InMemoryStringStore();
            new TransactionLedger(storage, Key).Add("tx-1");

            var afterRestart = new TransactionLedger(storage, Key);

            Assert.That(afterRestart.Contains("tx-1"), Is.True);
            Assert.That(afterRestart.Contains("tx-2"), Is.False);
        }

        [Test]
        public void IsBounded_AndForgetsTheOldestFirst()
        {
            var ledger = new TransactionLedger(new InMemoryStringStore(), Key, capacity: 3);

            ledger.Add("tx-1");
            ledger.Add("tx-2");
            ledger.Add("tx-3");
            ledger.Add("tx-4");

            Assert.That(ledger.Count, Is.EqualTo(3));
            Assert.That(ledger.Contains("tx-1"), Is.False);
            Assert.That(ledger.Contains("tx-2"), Is.True);
            Assert.That(ledger.Contains("tx-3"), Is.True);
            Assert.That(ledger.Contains("tx-4"), Is.True);
        }

        [Test]
        public void TheBoundAppliesToWhatIsPersisted()
        {
            var storage = new InMemoryStringStore();
            var ledger = new TransactionLedger(storage, Key, capacity: 2);

            ledger.Add("tx-1");
            ledger.Add("tx-2");
            ledger.Add("tx-3");

            Assert.That(storage.Read(Key), Is.EqualTo("tx-2\ntx-3"));
        }

        [Test]
        public void StaysWithinTheDefaultBoundUnderManyTransactions()
        {
            var ledger = new TransactionLedger(new InMemoryStringStore(), Key);

            for (int i = 0; i < TransactionLedger.DefaultCapacity + 50; i++)
            {
                ledger.Add("tx-" + i);
            }

            Assert.That(ledger.Count, Is.EqualTo(TransactionLedger.DefaultCapacity));
            Assert.That(ledger.Contains("tx-0"), Is.False);
            Assert.That(ledger.Contains("tx-49"), Is.False);
            Assert.That(ledger.Contains("tx-50"), Is.True);
            Assert.That(ledger.Contains("tx-" + (TransactionLedger.DefaultCapacity + 49)), Is.True);
        }

        [Test]
        public void ALoweredCapacity_TrimsTheOldestStoredEntries()
        {
            var storage = new InMemoryStringStore();
            var wide = new TransactionLedger(storage, Key, capacity: 4);
            wide.Add("tx-1");
            wide.Add("tx-2");
            wide.Add("tx-3");
            wide.Add("tx-4");

            var narrow = new TransactionLedger(storage, Key, capacity: 2);

            Assert.That(narrow.Count, Is.EqualTo(2));
            Assert.That(narrow.Contains("tx-2"), Is.False);
            Assert.That(narrow.Contains("tx-3"), Is.True);
            Assert.That(narrow.Contains("tx-4"), Is.True);
        }

        [Test]
        public void AnIdWithALineBreak_IsStillFoundAfterARestart()
        {
            var storage = new InMemoryStringStore();
            new TransactionLedger(storage, Key).Add("tx\n1");

            var afterRestart = new TransactionLedger(storage, Key);

            Assert.That(afterRestart.Contains("tx\n1"), Is.True);
            Assert.That(afterRestart.Count, Is.EqualTo(1));
        }

        [Test]
        public void AFailedWrite_StillRemembersTheIdForThisSession()
        {
            var ledger = new TransactionLedger(new ReadOnlyStringStore(), Key);

            Assert.Throws<InvalidOperationException>(() => ledger.Add("tx-1"));
            Assert.That(ledger.Contains("tx-1"), Is.True);
        }

        [Test]
        public void AFailedRead_IsNotTakenForAnEmptyLedger()
        {
            var storage = new FlakyStringStore();
            new TransactionLedger(storage, Key).Add("tx-stored");
            var ledger = new TransactionLedger(storage, Key);

            storage.FailingReads = 1;

            Assert.Throws<InvalidOperationException>(() => ledger.Contains("tx-stored"));
            Assert.That(ledger.Contains("tx-stored"), Is.True, "the read is tried again");
        }

        [Test]
        public void AnAddDuringAFailedRead_NeverReplacesTheStoredLedger()
        {
            var storage = new FlakyStringStore();
            new TransactionLedger(storage, Key).Add("tx-stored");
            int writesBefore = storage.Writes;
            var ledger = new TransactionLedger(storage, Key);

            storage.FailingReads = 1;
            Assert.Throws<InvalidOperationException>(() => ledger.Add("tx-new"));

            Assert.That(storage.Writes, Is.EqualTo(writesBefore), "nothing is written while the stored ledger is unknown");
            Assert.That(ledger.Contains("tx-new"), Is.True, "the id is still known for this session");

            ledger.Add("tx-later");

            Assert.That(storage.Read(Key), Is.EqualTo("tx-stored\ntx-new\ntx-later"));
        }

        [Test]
        public void AFailedWrite_IsCompletedByTheNextAdd()
        {
            var inner = new InMemoryStringStore();
            var storage = new SwitchableStringStore(inner);
            var ledger = new TransactionLedger(storage, Key);

            storage.FailWrites = true;
            Assert.Throws<InvalidOperationException>(() => ledger.Add("tx-1"));
            storage.FailWrites = false;
            ledger.Add("tx-2");

            Assert.That(inner.Read(Key), Is.EqualTo("tx-1\ntx-2"));
        }

        [Test]
        public void AFailedWrite_IsAlsoCompletedWhenTheSameIdIsAddedAgain()
        {
            var inner = new InMemoryStringStore();
            var storage = new SwitchableStringStore(inner);
            var ledger = new TransactionLedger(storage, Key);

            storage.FailWrites = true;
            Assert.Throws<InvalidOperationException>(() => ledger.Add("tx-1"));
            storage.FailWrites = false;

            Assert.That(ledger.Add("tx-1"), Is.False, "the id itself is not new");
            Assert.That(inner.Read(Key), Is.EqualTo("tx-1"));
        }

        [Test]
        public void RejectsUnusableArguments()
        {
            var storage = new InMemoryStringStore();

            Assert.Throws<ArgumentNullException>(() => new TransactionLedger(null, Key));
            Assert.Throws<ArgumentException>(() => new TransactionLedger(storage, string.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TransactionLedger(storage, Key, capacity: 0));
        }
    }
}
