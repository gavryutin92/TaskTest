using Dispenser.Protocol;
using NLog;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dispenser.Events;

namespace Dispenser
{
    public enum CardDispenserStatus
    {
        Idle,
        DispensingToRead,
        DispensingToExit,
        CapturingToRead,
        TakeCardWaiting,
        ReadCardWaiting,
        DispenserStatusWaiting,
        SpitCardWaiting
    }

    public interface ICardDispenserService
    {
        void Start(CardDispenserSettings settings);
        CardDispenserStatus Status { get; }

        event EventHandler<MessageHasComeEventArgs> MessageHasCome;
        event Action<object> ExceptionHasOut;
        event Action<object, Exception> ExceptionHasCome;
        /// <summary>
        /// Взять прочитать статус диспенсера
        /// </summary>
        /// <returns></returns>
        Task<bool> GetStatus();
        /// <summary>
        /// Выдвинуть карту на позицию ридера
        /// </summary>
        Task<bool> DispenseCardToRead();
        /// <summary>
        /// Выдвинуть карту на выход
        /// </summary>
        Task<bool> DispenseCardToExit();
        /// <summary>
        /// Втянуть карту на позицию ридера
        /// </summary>
        Task<bool> CaptureCardToRead();
        /// <summary>
        /// Выплюнуть карту
        /// </summary>
        Task<bool> SpitCard();
        /// <summary>
        /// Прерывает выполнение операции CaptureCardToRead
        /// </summary>
        void CancelCapture();
    }
}
