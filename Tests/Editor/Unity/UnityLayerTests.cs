using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Iap5PurchaseGuard.Tests
{
    [TestFixture]
    public class StoreSelectionTests
    {
        [Test]
        public void Android_NamesGooglePlay()
        {
            Assert.That(StoreSelection.ForPlatform(RuntimePlatform.Android), Is.EqualTo(GooglePlay.Name));
        }

        [TestCase(RuntimePlatform.IPhonePlayer)]
        [TestCase(RuntimePlatform.tvOS)]
        public void ApplePlatforms_NameTheAppStore(RuntimePlatform platform)
        {
            Assert.That(StoreSelection.ForPlatform(platform), Is.EqualTo(AppleAppStore.Name));
        }

        [TestCase(RuntimePlatform.OSXEditor)]
        [TestCase(RuntimePlatform.WindowsEditor)]
        [TestCase(RuntimePlatform.LinuxEditor)]
        [TestCase(RuntimePlatform.WindowsPlayer)]
        public void OtherPlatforms_KeepTheSdkDefault(RuntimePlatform platform)
        {
            Assert.That(StoreSelection.ForPlatform(platform), Is.Null);
        }
    }

    [TestFixture]
    public class CatalogMappingTests
    {
        [Test]
        public void EveryCatalogueEntry_BecomesAProductDefinition()
        {
            List<ProductDefinition> definitions = TestCatalog.Create().ToProductDefinitions();

            Assert.That(definitions, Has.Count.EqualTo(3));
            Assert.That(definitions[0].id, Is.EqualTo(TestCatalog.LanternOil));
            Assert.That(definitions[0].type, Is.EqualTo(ProductType.Consumable));
            Assert.That(definitions[1].id, Is.EqualTo(TestCatalog.MapExpansion));
            Assert.That(definitions[1].type, Is.EqualTo(ProductType.NonConsumable));
            Assert.That(definitions[2].id, Is.EqualTo(TestCatalog.ExplorerClub));
            Assert.That(definitions[2].type, Is.EqualTo(ProductType.Subscription));
        }
    }

    [TestFixture]
    public class ReceiptGatesTests
    {
        private static FakeOrder PlayOrder()
        {
            return new FakeOrder
            {
                TransactionId = "purchase-token",
                ProductId = TestCatalog.MapExpansion,
                StoreProductId = TestCatalog.MapExpansion,
                StoreName = GooglePlay.Name,
                Receipt = "signed-purchase-data"
            };
        }

        [Test]
        public void Android_UsesThePlayValidator()
        {
            IReceiptGate gate = ReceiptGates.ForPlatform(RuntimePlatform.Android, PlayLicenseKey.Placeholder, "com.example.game");

            Assert.That(gate, Is.InstanceOf<PlayReceiptValidator>());
        }

        [Test]
        public void ApplePlatforms_RelyOnThePlatform()
        {
            IReceiptGate gate = ReceiptGates.ForPlatform(RuntimePlatform.IPhonePlayer, null, "com.example.game");

            ReceiptCheck check = gate.Check(PlayOrder());

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Valid));
            Assert.That(check.Reason, Is.EqualTo(VerdictReason.VerifiedByPlatform));
        }

        [Test]
        public void ApplePlatformsOnStoreKit1_SayThatNothingWasChecked()
        {
            IReceiptGate gate = ReceiptGates.ForPlatform(RuntimePlatform.IPhonePlayer, null, "com.example.game", storeKit1: true);

            ReceiptCheck check = gate.Check(PlayOrder());

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Valid));
            Assert.That(check.Reason, Is.EqualTo(VerdictReason.NotEnforcedOnThisPlatform));
        }

        [Test]
        public void TheEditor_HasNothingToCheck()
        {
            IReceiptGate gate = ReceiptGates.ForPlatform(RuntimePlatform.OSXEditor, null, "com.example.game");

            ReceiptCheck check = gate.Check(PlayOrder());

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Valid));
            Assert.That(check.Reason, Is.EqualTo(VerdictReason.NotEnforcedOnThisPlatform));
        }

        [Test]
        public void PlayValidator_WithoutAKey_IsUnavailable()
        {
            var validator = new PlayReceiptValidator(PlayLicenseKey.Placeholder, "com.example.game");

            ReceiptCheck check = validator.Check(PlayOrder());

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Unavailable));
            Assert.That(check.Reason, Is.EqualTo(VerdictReason.KeyNotConfigured));
        }

        [Test]
        public void PlayValidator_RefusesATestStoreReceipt_EvenWithoutAKey()
        {
            var validator = new PlayReceiptValidator(null, "com.example.game");
            FakeOrder order = PlayOrder();
            order.StoreName = PurchaseFlowOptions.SdkTestStoreName;

            ReceiptCheck check = validator.Check(order);

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Invalid));
            Assert.That(check.Reason, Is.EqualTo(VerdictReason.WrongStore));
        }
    }

    [TestFixture]
    public class UnityStoreOrderTests
    {
        private sealed class EmptyCart : ICart
        {
            private static readonly CartItem[] NoItems = new CartItem[0];

            public IReadOnlyList<CartItem> Items()
            {
                return NoItems;
            }
        }

        private sealed class StubOrderInfo : IOrderInfo
        {
            public IAppleOrderInfo Apple
            {
                get { return null; }
            }

            public IGoogleOrderInfo Google
            {
                get { return null; }
            }

            public IPaymentProvidersOrderInfo PaymentProviders
            {
                get { return null; }
            }

            public List<IPurchasedProductInfo> PurchasedProductInfo { get; set; }

            public string Receipt { get; set; }

            public string TransactionID { get; set; }
        }

        [Test]
        public void ReadsTheTransactionIdAndTheStoreStampedOnTheReceipt()
        {
            var info = new StubOrderInfo
            {
                TransactionID = "purchase-token",
                Receipt = "{\"Payload\":\"data\",\"Store\":\"GooglePlay\",\"TransactionID\":\"purchase-token\"}"
            };

            var order = new UnityStoreOrder(new PendingOrder(new EmptyCart(), info));

            Assert.That(order.TransactionId, Is.EqualTo("purchase-token"));
            Assert.That(order.StoreName, Is.EqualTo(GooglePlay.Name));
            Assert.That(order.Receipt, Is.EqualTo(info.Receipt));
        }

        [Test]
        public void AnUnreadableReceipt_HasNoStoreName()
        {
            var info = new StubOrderInfo { TransactionID = "purchase-token", Receipt = "not a receipt envelope" };

            var order = new UnityStoreOrder(new PendingOrder(new EmptyCart(), info));

            Assert.That(order.StoreName, Is.Empty);
        }

        [Test]
        public void AnOrderWithoutExactlyOneProduct_HasNoProductId()
        {
            var info = new StubOrderInfo { TransactionID = "purchase-token", Receipt = string.Empty };

            var order = new UnityStoreOrder(new PendingOrder(new EmptyCart(), info));

            Assert.That(order.ProductId, Is.Empty);
            Assert.That(order.StoreProductId, Is.Empty);
        }

        [Test]
        public void ANullOrder_ReadsAsEmpty()
        {
            var order = new UnityStoreOrder(null);

            Assert.That(order.TransactionId, Is.Empty);
            Assert.That(order.ProductId, Is.Empty);
            Assert.That(order.StoreName, Is.Empty);
            Assert.That(order.Receipt, Is.Empty);
        }
    }
}
