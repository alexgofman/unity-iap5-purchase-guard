using NUnit.Framework;

namespace Iap5PurchaseGuard.Tests
{
    [TestFixture]
    public class VerdictPolicyTests
    {
        [TestCase(ReceiptVerdict.Valid, UnavailablePolicy.FailOpen, GateDecision.Grant)]
        [TestCase(ReceiptVerdict.Valid, UnavailablePolicy.FailClosed, GateDecision.Grant)]
        [TestCase(ReceiptVerdict.Invalid, UnavailablePolicy.FailOpen, GateDecision.Refuse)]
        [TestCase(ReceiptVerdict.Invalid, UnavailablePolicy.FailClosed, GateDecision.Refuse)]
        [TestCase(ReceiptVerdict.Deferred, UnavailablePolicy.FailOpen, GateDecision.Hold)]
        [TestCase(ReceiptVerdict.Deferred, UnavailablePolicy.FailClosed, GateDecision.Hold)]
        [TestCase(ReceiptVerdict.Unavailable, UnavailablePolicy.FailOpen, GateDecision.Grant)]
        [TestCase(ReceiptVerdict.Unavailable, UnavailablePolicy.FailClosed, GateDecision.Refuse)]
        public void DecisionTable(ReceiptVerdict verdict, UnavailablePolicy policy, GateDecision expected)
        {
            Assert.That(new VerdictPolicy(policy).Decide(verdict), Is.EqualTo(expected));
        }

        [Test]
        public void AnUnknownVerdict_IsRefused()
        {
            var unknown = (ReceiptVerdict)99;

            Assert.That(new VerdictPolicy(UnavailablePolicy.FailOpen).Decide(unknown), Is.EqualTo(GateDecision.Refuse));
        }

        [Test]
        public void AnUninitialisedCheck_IsInvalid()
        {
            ReceiptCheck uninitialised = default(ReceiptCheck);

            Assert.That(uninitialised.Verdict, Is.EqualTo(ReceiptVerdict.Invalid));
            Assert.That(new VerdictPolicy(UnavailablePolicy.FailOpen).Decide(uninitialised.Verdict), Is.EqualTo(GateDecision.Refuse));
        }

        [Test]
        public void TheDefaultDecision_IsRefuse()
        {
            Assert.That(default(GateDecision), Is.EqualTo(GateDecision.Refuse));
        }
    }
}
