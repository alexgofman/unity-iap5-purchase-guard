using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Iap5PurchaseGuard.Tests
{
    [TestFixture]
    public class BackoffScheduleTests
    {
        private static List<double> DelaysOf(BackoffSchedule schedule)
        {
            var delays = new List<double>();
            for (int attempt = 0; schedule.TryGetDelay(attempt, out double delay); attempt++)
            {
                delays.Add(delay);
            }

            return delays;
        }

        [Test]
        public void DefaultSchedule_DoublesUpToTheCap_ThenStops()
        {
            var schedule = new BackoffSchedule();

            Assert.That(DelaysOf(schedule), Is.EqualTo(new[] { 2d, 4d, 8d, 16d, 32d, 60d }));
            Assert.That(schedule.MaxAttempts, Is.EqualTo(6));
        }

        [Test]
        public void GivesUpAfterTheLastAttempt()
        {
            var schedule = new BackoffSchedule(maxAttempts: 2);

            Assert.That(schedule.TryGetDelay(1, out double _), Is.True);
            Assert.That(schedule.TryGetDelay(2, out double delay), Is.False);
            Assert.That(delay, Is.EqualTo(0d));
        }

        [Test]
        public void CustomSchedule_IsHonoured()
        {
            var schedule = new BackoffSchedule(firstDelaySeconds: 1d, multiplier: 3d, maxDelaySeconds: 20d, maxAttempts: 4);

            Assert.That(DelaysOf(schedule), Is.EqualTo(new[] { 1d, 3d, 9d, 20d }));
        }

        [Test]
        public void MultiplierOfOne_GivesAConstantDelay()
        {
            var schedule = new BackoffSchedule(firstDelaySeconds: 5d, multiplier: 1d, maxDelaySeconds: 5d, maxAttempts: 3);

            Assert.That(DelaysOf(schedule), Is.EqualTo(new[] { 5d, 5d, 5d }));
        }

        [Test]
        public void VeryLateAttempts_StayAtTheCap()
        {
            var schedule = new BackoffSchedule(maxAttempts: 5000);

            Assert.That(schedule.TryGetDelay(4999, out double delay), Is.True);
            Assert.That(delay, Is.EqualTo(60d));
        }

        [Test]
        public void ZeroAttempts_NeverRetries()
        {
            var schedule = new BackoffSchedule(maxAttempts: 0);

            Assert.That(schedule.TryGetDelay(0, out double _), Is.False);
        }

        [Test]
        public void NegativeAttempt_IsNotAnAttempt()
        {
            Assert.That(new BackoffSchedule().TryGetDelay(-1, out double _), Is.False);
        }

        [Test]
        public void RejectsSchedulesThatMakeNoSense()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BackoffSchedule(firstDelaySeconds: 0d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BackoffSchedule(multiplier: 0.5d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BackoffSchedule(firstDelaySeconds: 10d, maxDelaySeconds: 5d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BackoffSchedule(maxAttempts: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BackoffSchedule(firstDelaySeconds: double.NaN));
        }
    }
}
