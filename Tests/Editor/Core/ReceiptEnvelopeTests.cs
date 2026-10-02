using NUnit.Framework;

namespace Iap5PurchaseGuard.Tests
{
    [TestFixture]
    public class ReceiptEnvelopeTests
    {
        [Test]
        public void ReadsTheStoreOfAGooglePlayEnvelope()
        {
            // The payload is itself JSON, carried as an escaped string, the way the SDK writes it.
            string receipt =
                "{\"Payload\":\"{\\\"json\\\":\\\"{\\\\\\\"orderId\\\\\\\":\\\\\\\"order-1\\\\\\\",\\\\\\\"productId\\\\\\\":\\\\\\\"map_expansion\\\\\\\"}\\\",\\\"signature\\\":\\\"c2lnbmF0dXJl\\\"}\","
                + "\"Store\":\"GooglePlay\",\"TransactionID\":\"purchase-token\"}";

            Assert.That(ReceiptEnvelope.ReadStoreName(receipt), Is.EqualTo("GooglePlay"));
        }

        [Test]
        public void ReadsTheStoreOfATestStoreEnvelope()
        {
            string receipt = "{\"Payload\":\"test data\",\"Store\":\"fake\",\"TransactionID\":\"0c1f\"}";

            Assert.That(ReceiptEnvelope.ReadStoreName(receipt), Is.EqualTo(PurchaseFlowOptions.SdkTestStoreName));
        }

        [Test]
        public void StoreTextInsideThePayload_IsNotTakenForTheStore()
        {
            string receipt = "{\"Payload\":\"{\\\"Store\\\":\\\"GooglePlay\\\"}\",\"Store\":\"fake\",\"TransactionID\":\"t\"}";

            Assert.That(ReceiptEnvelope.ReadStoreName(receipt), Is.EqualTo("fake"));
        }

        [Test]
        public void PayloadThatOnlyPretendsToNameAStore_LeavesTheStoreEmpty()
        {
            string receipt = "{\"Payload\":\"\\\",\\\"Store\\\":\\\"GooglePlay\\\",\\\"x\\\":\\\"\",\"TransactionID\":\"t\"}";

            Assert.That(ReceiptEnvelope.ReadStoreName(receipt), Is.Empty);
        }

        [Test]
        public void MemberOrderAndWhitespace_DoNotMatter()
        {
            string receipt = " {\r\n\t\"TransactionID\" : \"t\" ,\r\n\t\"Store\" : \"AppleAppStore\" ,\r\n\t\"Payload\" : \"cGF5bG9hZA==\"\r\n} \n";

            Assert.That(ReceiptEnvelope.ReadStoreName(receipt), Is.EqualTo("AppleAppStore"));
        }

        [Test]
        public void PayloadEndingInABackslash_DoesNotSwallowTheNextMember()
        {
            string receipt = "{\"Payload\":\"ends with a backslash \\\\\",\"Store\":\"GooglePlay\",\"TransactionID\":\"t\"}";

            Assert.That(ReceiptEnvelope.ReadStoreName(receipt), Is.EqualTo("GooglePlay"));
        }

        [Test]
        public void OtherValueTypes_AreSkipped()
        {
            string receipt = "{\"a\":1.5e3,\"b\":true,\"c\":null,\"d\":[1,\"]\",{\"Store\":\"nested\"}],\"e\":{\"Store\":\"nested\",\"f\":\"}\"},\"Store\":\"GooglePlay\"}";

            Assert.That(ReceiptEnvelope.ReadStoreName(receipt), Is.EqualTo("GooglePlay"));
        }

        [Test]
        public void StoreNamedOnlyInsideANestedObject_IsNotRead()
        {
            string receipt = "{\"Payload\":\"p\",\"Extra\":{\"Store\":\"GooglePlay\"}}";

            Assert.That(ReceiptEnvelope.ReadStoreName(receipt), Is.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not a receipt envelope")]
        [TestCase("[\"Store\",\"GooglePlay\"]")]
        [TestCase("\"Store\":\"GooglePlay\"")]
        [TestCase("{}")]
        [TestCase("{\"Payload\":\"p\"}")]
        [TestCase("{\"Store\":\"GooglePlay\"")]
        [TestCase("{\"Store\":\"GooglePlay")]
        [TestCase("{\"Store\":\"GooglePlay\",}")]
        [TestCase("{\"Store\":\"GooglePlay\"} trailing")]
        [TestCase("{\"Store\":\"GooglePlay\"}{\"Store\":\"fake\"}")]
        [TestCase("{\"Store\":\"GooglePlay\",\"Store\":\"fake\"}")]
        [TestCase("{\"Store\":\"fake\",\"Store\":\"GooglePlay\"}")]
        [TestCase("{\"Store\":7}")]
        [TestCase("{\"Store\":null}")]
        [TestCase("{\"Store\":{\"name\":\"GooglePlay\"}}")]
        [TestCase("{\"Store\":[\"GooglePlay\"]}")]
        [TestCase("{\"Store\":\"Google\\u0050lay\"}")]
        [TestCase("{\"St\\u006fre\":\"GooglePlay\"}")]
        [TestCase("{\"store\":\"GooglePlay\"}")]
        [TestCase("{\"Store\" \"GooglePlay\"}")]
        [TestCase("{\"Payload\":\"unterminated,\"Store\":\"GooglePlay\"}")]
        [TestCase("{\"Payload\":{\"open\":1,\"Store\":\"GooglePlay\"}")]
        [TestCase("{\"Payload\":\"p\\")]
        public void AnythingUnexpected_GivesAnEmptyStoreName(string receipt)
        {
            Assert.That(ReceiptEnvelope.ReadStoreName(receipt), Is.Empty);
        }

        [Test]
        public void AnEmptyStoreValue_IsReadAsEmpty()
        {
            Assert.That(ReceiptEnvelope.ReadStoreName("{\"Store\":\"\"}"), Is.Empty);
        }
    }
}
