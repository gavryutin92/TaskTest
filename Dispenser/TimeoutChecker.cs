using System;

namespace Dispenser
{
    public class TimeoutChecker
    {
        /// <summary>
        /// Время начала, миллисек.
        /// </summary>
        public long StartTime { get; set; }
        /// <summary>
        /// Таймаут, миллисек.
        /// </summary>
        public long Timeout { get; set; }

        /// <inheritdoc />
        public TimeoutChecker(long timeout)
        {
            Timeout = timeout;
            StartTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        }
        /// <summary>
        /// Проверяет, истекло ли указанное время
        /// </summary>
        /// <returns></returns>
        public bool Check()
        {
            var now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            return ((now - StartTime) > Timeout);
        }
    }
}
