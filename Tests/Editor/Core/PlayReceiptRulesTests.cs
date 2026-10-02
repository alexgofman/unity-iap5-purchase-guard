using System;
using NUnit.Framework;

namespace Iap5PurchaseGuard.Tests
{
    [TestFixture]
    public class PlayReceiptRulesTests
    {
        private const string PlayStore = "GooglePlay";
        private const string Product = TestCatalog.MapExpansion;
        private const string Token = "purchase-token";

        private sealed class ScriptedSignatureCheck : IPlaySignatureCheck
        {
            public PlaySignatureResult Result { get; set; }

            public int Calls { get; private set; }

            public PlaySignatureResult Verify(string receipt)
            {
                Calls++;
                return Result;
            }
        }

        private static FakeOrder GenuineOrder()
        {
            return new FakeOrder
            {
                TransactionId = Token,
                ProductId = Product,
                StoreProductId = Product,
                StoreName = PlayStore,
                Receipt = "signed-purchase-data"
            };
        }

        private static ScriptedSignatureCheck Signed(params PlayPurchase[] purchases)
        {
            return new ScriptedSignatureCheck { Result = new PlaySignatureResult(PlaySignatureStatus.Verified, purchases) };
        }

        private static ScriptedSignatureCheck Status(PlaySignatureStatus status)
        {
            return new ScriptedSignatureCheck { Result = new PlaySignatureResult(status) };
        }

        [Test]
        public void SignedPurchaseForTheOrderedProduct_IsValid()
        {
            ReceiptCheck check = PlayReceiptRules.Evaluate(GenuineOrder(), PlayStore, Signed(new PlayPurchase(Product, Token, true)));

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Valid));
            Assert.That(check.Reason, Is.EqualTo(VerdictReason.SignatureVerified));
        }

        [Test]
        public void SignedPurchaseIsMatchedAgainstTheStoreProductId()
        {
            FakeOrder order = GenuineOrder();
            order.StoreProductId = "store_side_id";

            ReceiptCheck matchesStoreId = PlayReceiptRules.Evaluate(order, PlayStore, Signed(new PlayPurchase("store_side_id", Token, true)));
            ReceiptCheck matchesCatalogueIdOnly = PlayReceiptRules.Evaluate(order, PlayStore, Signed(new PlayPurchase(Product, Token, true)));

            Assert.That(matchesStoreId.Verdict, Is.EqualTo(ReceiptVerdict.Valid));
            Assert.That(matchesCatalogueIdOnly.Verdict, Is.EqualTo(ReceiptVerdict.Invalid));
            Assert.That(matchesCatalogueIdOnly.Reason, Is.EqualTo(VerdictReason.WrongProduct));
        }

        [Test]
        public void OrderWithoutAStoreProductId_IsMatchedAgainstTheCatalogueId()
        {
            FakeOrder order = GenuineOrder();
            order.StoreProductId = string.Empty;

            ReceiptCheck check = PlayReceiptRules.Evaluate(order, PlayStore, Signed(new PlayPurchase(Product, Token, true)));

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Valid));
        }

        [Test]
        public void SignedPurchaseReplayedUnderAnotherTransactionId_IsInvalid()
        {
            // Real, correctly signed purchase data, presented again with an invented transaction id.
            FakeOrder order = GenuineOrder();
            order.TransactionId = "invented-token";

            ReceiptCheck check = PlayReceiptRules.Evaluate(order, PlayStore, Signed(new PlayPurchase(Product, Token, true)));

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Invalid));
            Assert.That(check.Reason, Is.EqualTo(VerdictReason.WrongTransaction));
        }

        [Test]
        public void SignedPurchaseWithoutAToken_IsInvalid()
        {
            ReceiptCheck check = PlayReceiptRules.Evaluate(GenuineOrder(), PlayStore, Signed(new PlayPurchase(Product, null, true)));

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Invalid));
            Assert.That(check.Reason, Is.EqualTo(VerdictReason.WrongTransaction));
        }

        [Test]
        public void VerifiedResultWithNoPurchaseInIt_IsInvalid()
        {
            ReceiptCheck check = PlayReceiptRules.Evaluate(GenuineOrder(), PlayStore, Signed());

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Invalid));
            Assert.That(check.Reason, Is.EqualTo(VerdictReason.NoStoreReceipt));
        }

        [Test]
        public void SignedPurchaseForAnotherProduct_IsInvalid()
        {
            ReceiptCheck check = PlayReceiptRules.Evaluate(GenuineOrder(), PlayStore, Signed(new PlayPurchase(TestCatalog.LanternOil, Token, true)));

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Invalid));
            Assert.That(check.Reason, Is.EqualTo(VerdictReason.WrongProduct));
        }

        [Test]
        public void SignedPurchaseWithoutAProductId_IsInvalid()
        {
            ReceiptCheck check = PlayReceiptRules.Evaluate(GenuineOrder(), PlayStore, Signed(new PlayPurchase(null, Token, true)));

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Invalid));
            Assert.That(check.Reason, Is.EqualTo(VerdictReason.WrongProduct));
        }

        [Test]
        public void SignedPurchaseThatIsNotPaid_IsDeferred()
        {
            ReceiptCheck check = PlayReceiptRules.Evaluate(GenuineOrder(), PlayStore, Signed(new PlayPurchase(Product, Token, false)));

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Deferred));
            Assert.That(check.Reason, Is.EqualTo(VerdictReason.NotPurchased));
        }

        [TestCase(PlaySignatureStatus.SignatureMismatch, VerdictReason.SignatureMismatch)]
        [TestCase(PlaySignatureStatus.WrongApplication, VerdictReason.WrongApplication)]
        [TestCase(PlaySignatureStatus.ReceiptUnreadable, VerdictReason.ReceiptUnreadable)]
        [TestCase(PlaySignatureStatus.Rejected, VerdictReason.ValidatorRejected)]
        public void FailedSignatureCheck_IsInvalid(PlaySignatureStatus status, VerdictReason expectedReason)
        {
            ReceiptCheck check = PlayReceiptRules.Evaluate(GenuineOrder(), PlayStore, Status(status));

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Invalid));
            Assert.That(check.Reason, Is.EqualTo(expectedReason));
        }

        [TestCase(PlaySignatureStatus.KeyNotConfigured, VerdictReason.KeyNotConfigured)]
        [TestCase(PlaySignatureStatus.KeyUnusable, VerdictReason.KeyUnusable)]
        public void MissingOrUnusableKey_IsUnavailable(PlaySignatureStatus status, VerdictReason expectedReason)
        {
            ReceiptCheck check = PlayReceiptRules.Evaluate(GenuineOrder(), PlayStore, Status(status));

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Unavailable));
            Assert.That(check.Reason, Is.EqualTo(expectedReason));
        }

        [Test]
        public void SignatureCheckThatReturnsNothing_IsInvalid()
        {
            ReceiptCheck check = PlayReceiptRules.Evaluate(GenuineOrder(), PlayStore, new ScriptedSignatureCheck { Result = null });

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Invalid));
        }

        // The structural checks need no key. Each case below is refused even when the key is
        // missing, so none of them can reach the verdict that may fail open.
        [TestCase("no receipt", VerdictReason.ReceiptMissing)]
        [TestCase("no transaction id", VerdictReason.TransactionIdMissing)]
        [TestCase("stamped by the test store", VerdictReason.WrongStore)]
        [TestCase("stamped by another store", VerdictReason.WrongStore)]
        [TestCase("no store stamp", VerdictReason.WrongStore)]
        [TestCase("no product", VerdictReason.ProductUnknown)]
        public void StructurallyBrokenOrder_IsInvalidEvenWithoutAKey(string defect, VerdictReason expectedReason)
        {
            FakeOrder order = GenuineOrder();
            switch (defect)
            {
                case "no receipt":
                    order.Receipt = string.Empty;
                    break;
                case "no transaction id":
                    order.TransactionId = string.Empty;
                    break;
                case "stamped by the test store":
                    order.StoreName = PurchaseFlowOptions.SdkTestStoreName;
                    break;
                case "stamped by another store":
                    order.StoreName = "AppleAppStore";
                    break;
                case "no store stamp":
                    order.StoreName = string.Empty;
                    break;
                case "no product":
                    order.ProductId = string.Empty;
                    order.StoreProductId = string.Empty;
                    break;
                default:
                    Assert.Fail("Unknown defect: " + defect);
                    break;
            }

            ScriptedSignatureCheck keyMissing = Status(PlaySignatureStatus.KeyNotConfigured);

            ReceiptCheck check = PlayReceiptRules.Evaluate(order, PlayStore, keyMissing);

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Invalid));
            Assert.That(check.Reason, Is.EqualTo(expectedReason));
            Assert.That(keyMissing.Calls, Is.EqualTo(0), "the receipt never reaches the signature check");
        }

        [Test]
        public void NullOrder_IsInvalid()
        {
            ReceiptCheck check = PlayReceiptRules.Evaluate(null, PlayStore, Status(PlaySignatureStatus.Verified));

            Assert.That(check.Verdict, Is.EqualTo(ReceiptVerdict.Invalid));
        }

        [Test]
        public void MissingSignatureCheck_IsACallerError()
        {
            Assert.Throws<ArgumentNullException>(() => PlayReceiptRules.Evaluate(GenuineOrder(), PlayStore, null));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("   ", false)]
        [TestCase(PlayLicenseKey.Placeholder, false)]
        [TestCase("  " + PlayLicenseKey.Placeholder + "  ", false)]
        [TestCase("AnythingElseCountsAsAKey", true)]
        public void LicenseKey_IsConfiguredOnlyWhenItIsNotBlankOrThePlaceholder(string key, bool expected)
        {
            Assert.That(PlayLicenseKey.IsConfigured(key), Is.EqualTo(expected));
        }
    }
}
