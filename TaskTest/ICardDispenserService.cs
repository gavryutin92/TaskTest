using CardDispenserServiceNs.Protocol;
using NLog;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CardDispenserServiceNs.Events;

namespace CardDispenserServiceNs
{
    public enum CardDispenserStatus
    {
        Off,
        Idle,
        Error,
        DispensingToRead,
        DispensingToExit,
        CapturingToRead,
        CapturingToError,
        TakeCardWaiting,
        ReadCardWaiting,
    }

    public interface ICardDispenserService
    {
        Task Reset();
        Task Start(CardDispenserSettings settings);
        CardDispenserStatus Status { get; }
        bool IsEmpty { get; }

        /// <summary>
        /// Возникает при изменении статуса
        /// </summary>
        event Action<CardDispenserStatus> StatusChanged;
        event EventHandler<MessageHasComeEventArgs> MessageHasCome;
        event Action<object> ExceptionHasOut;
        event Action<object, Exception> ExceptionHasCome;
        /// <summary>
        /// Запретить прием карт
        /// </summary>
        void ProhibitCart();
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
        /// Втянуть карту в сброс
        /// </summary>
        Task<bool> CaptureCardToError();
        /// <summary>
        /// Выплюнуть карту
        /// </summary>
        Task<bool> SpitCard();
        /// <summary>
        /// Прерывает выполнение операции
        /// </summary>
        void Abort();
    }
}
