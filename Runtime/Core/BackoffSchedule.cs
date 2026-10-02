using System;

namespace Iap5PurchaseGuard
{
    /// <summary>
    /// Delays between automatic store reconnect attempts: exponential, capped, and limited in
    /// number. Once the attempts are used up the caller stops retrying on its own and waits for a
    /// player action (a purchase or restore tap), which starts a new series.
    /// </summary>
    /// <remarks>
    /// There is no random jitter on purpose. The other end is the store service on the same
    /// device, not a shared server, so there is no crowd of clients to spread out.
    /// </remarks>
    public sealed class BackoffSchedule
    {
        private readonly double _firstDelaySeconds;
        private readonly double _multiplier;
        private readonly double _maxDelaySeconds;

        /// <summary>The defaults give delays of 2, 4, 8, 16, 32 and 60 seconds.</summary>
        public BackoffSchedule(
            double firstDelaySeconds = 2d,
            double multiplier = 2d,
            double maxDelaySeconds = 60d,
            int maxAttempts = 6)
        {
            if (!(firstDelaySeconds > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(firstDelaySeconds), "The first delay must be greater than zero.");
            }

            if (!(multiplier >= 1d))
            {
                throw new ArgumentOutOfRangeException(nameof(multiplier), "The multiplier must be at least 1.");
            }

            if (!(maxDelaySeconds >= firstDelaySeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(maxDelaySeconds), "The maximum delay must not be shorter than the first delay.");
            }

            if (maxAttempts < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAttempts), "The number of attempts must not be negative.");
            }

            _firstDelaySeconds = firstDelaySeconds;
            _multiplier = multiplier;
            _maxDelaySeconds = maxDelaySeconds;
            MaxAttempts = maxAttempts;
        }

        /// <summary>Number of automatic retries before giving up.</summary>
        public int MaxAttempts { get; }

        /// <summary>
        /// Gets the delay before retry number <paramref name="attempt"/> (zero-based). Returns false
        /// when the automatic attempts are used up.
        /// </summary>
        public bool TryGetDelay(int attempt, out double delaySeconds)
        {
            if (attempt < 0 || attempt >= MaxAttempts)
            {
                delaySeconds = 0d;
                return false;
            }

            // Math.Pow overflows to infinity for large attempts; the comparison still picks the cap.
            double delay = _firstDelaySeconds * Math.Pow(_multiplier, attempt);
            delaySeconds = delay < _maxDelaySeconds ? delay : _maxDelaySeconds;
            return true;
        }
    }
}
