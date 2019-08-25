using CardDispenserServiceNs.Protocol;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CardDispenserServiceNs.Events;

namespace CardDispenserServiceNs
{
    public enum State
    {
        NoConnect,
        WaitAskNak,
        Connected,
        CheckingStatus,
        WaitingAsk,
        WaitingCommand,
        WaitingStatus
    }

    public class CardDispenserService //: ICardDispenserService
    {
        private Command _command;

        public Command Command
        {
            get => _command;
            private set
            {
                if (_command != null)
                {
                    if((_command is HighClassStatusCheckingCommand) == false)
                        PrevCommand = _command;
                }

                _command = value;
            }
        }
        public Command PrevCommand { get; private set; }

        public State State { get; set; }

        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private static SerialPort _port;
        private bool _isConnected;
        private bool _isException;

        private volatile bool _isWaitingFull;
        private volatile bool _isWaitingEmpty;
        private volatile bool _isAborted = false;
        public bool CardAtReadingPosition { get; private set; }
        private volatile bool _isWaitingAskNak;
        private volatile bool _isWaitingCardEmptySensorStatus;
        private bool _haveCardInWork = true;

        private TaskCompletionSource<bool> _taskCompletionSource;
        private DateTime _startTakeCardTime = DateTime.MaxValue;

        public event Action<CardDispenserStatus> StatusChanged;
        public event EventHandler<MessageHasComeEventArgs> MessageHasCome;
        public event Action<object> ExceptionHasOut;
        public event Action<object, Exception> ExceptionHasCome;

        private CardDispenserSettings _settings;

        private CardDispenserStatus _status;
        public CardDispenserStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                StatusChanged?.Invoke(Status);
            }
        }

        public bool IsEmpty { get; private set; }

        public CardDispenserService()
        {
            Status = CardDispenserStatus.Idle;
        }

        private void OnExceptionHasOut()
        {
            if (_isException)
            {
                _isException = false;
                ExceptionHasOut?.Invoke(this);
            }
        }

        void OnExceptionHasCome(Exception e)
        {
            _log.Error(e);
            _isConnected = false;
            _isException = true;
            ExceptionHasCome?.Invoke(this, e);
        }

        public Task<bool> CaptureCardToRead()
        {
            _taskCompletionSource = new TaskCompletionSource<bool>();
            _isWaitingFull = true;
            Command = new ReadCapturePolicyCommand();
            return _taskCompletionSource.Task;
        }

        public Task<bool> DispenseCardToExit()
        {
            _startTakeCardTime = DateTime.Now;
            _taskCompletionSource = new TaskCompletionSource<bool>();
            _isWaitingEmpty = true;
            Status = CardDispenserStatus.TakeCardWaiting;
            Command = new DispenseCardToExitPosCommand();
            return _taskCompletionSource.Task;
        }

        public Task<bool> DispenseCardToRead()
        {
            _taskCompletionSource = new TaskCompletionSource<bool>();
            _isWaitingFull = true;
            Command = new DispenseCardToReadPosCommand();
            return _taskCompletionSource.Task;
        }

        public Task<bool> GetCardEmptySensorStatus()
        {
            _taskCompletionSource = new TaskCompletionSource<bool>();
            _isWaitingCardEmptySensorStatus = true;
            return _taskCompletionSource.Task;
        }

        public void Connect()
        {
            _port?.Dispose();
            //_port = new SerialPort(_settings.Name);
            //_port.BaudRate = _settings.BaudRate;
            //_port.Parity = _settings.Parity;
            //_port.DataBits = _settings.DataBits;
            //_port.StopBits = _settings.StopBits;
            //_port.ReadTimeout = _settings.ReadWriteTimeout;
            //_port.WriteTimeout = _settings.ReadWriteTimeout;
            _port = new SerialPort("COM1");
            _port.BaudRate = 9600;
            _port.Parity = Parity.None;
            _port.DataBits = 8;
            _port.StopBits = StopBits.One;
            _port.ReadTimeout = 500;
            _port.WriteTimeout = 500;
            _port.DtrEnable = true;
            _port.RtsEnable = true;
            _port.Open();
        }

        private void HandleSensor1Status(ApStatusEnum resp)
        {
            if (resp.HasFlag(ApStatusEnum.CaptSensor1Status))
            {
                CardAtReadingPosition = true;
                if (_isWaitingFull)
                {
                    _isWaitingFull = false;
                    _taskCompletionSource.SetResult(true);
                }
            }
            else
            {
                CardAtReadingPosition = false;
                if (_isWaitingEmpty)
                {
                    _isWaitingEmpty = false;
                    Status = CardDispenserStatus.DispensingToExit;
                }
            }
        }

        private void HandleResponse(string mes)
        {
            var resp = ApResponse.Parse(mes);
            HandleErrors(resp);
            HandleWarnings(resp);
            HandleCardEmptySensorStatus(resp);
            HandleSensor1Status(resp);
        }

        private void HandleCardEmptySensorStatus(ApStatusEnum resp)
        {
            if (resp.HasFlag(ApStatusEnum.CardEmptySensorStatus))
            {
                IsEmpty = true;
                if (_isWaitingCardEmptySensorStatus)
                {
                    _isWaitingCardEmptySensorStatus = false;
                    _taskCompletionSource.SetResult(true);
                }
            }
            else
            {
                IsEmpty = false;
                if (_isWaitingCardEmptySensorStatus)
                {
                    _isWaitingCardEmptySensorStatus = false;
                    _taskCompletionSource.SetResult(false);
                }
            }
        }

        private bool _prevCardPreEmpty;

        private void HandleWarnings(ApStatusEnum resp)
        {
            if (resp.HasFlag(ApStatusEnum.CardPreEmpty))
            {
                if(_prevCardPreEmpty == false)
                {
                    _prevCardPreEmpty = true;
                    OnWarning(ApStatusEnum.CardPreEmpty.ToString());
                }
            }
            else
            {
                if (_prevCardPreEmpty == true)
                    _prevCardPreEmpty = false;
            }
            if (resp.HasFlag(ApStatusEnum.CardCaptureError))
            {
                OnWarning(ApStatusEnum.CardCaptureError.ToString());
                Reset();
            }
        }

        private void OnWarning(string warning)
        {
            _log.Warn(warning);
            MessageHasCome?.Invoke(this, new MessageHasComeEventArgs(MessageType.Warn, warning));
        }

        private List<ApStatusEnum> _errorsFlags = new List<ApStatusEnum>();

        private void HandleErrors(ApStatusEnum resp)
        {
            if (resp.HasFlag(ApStatusEnum.FailureAlarmSensorInvalid))
            {
                OnError(ApStatusEnum.FailureAlarmSensorInvalid);
            }
            if (resp.HasFlag(ApStatusEnum.ErrorCardBinIsFull))
            {
                OnError(ApStatusEnum.ErrorCardBinIsFull);
            }
            if(resp.HasFlag(ApStatusEnum.CardDispenseError))
            {
                OnError(ApStatusEnum.CardDispenseError);
            }
            if (resp.HasFlag(ApStatusEnum.NoCapture))
            {
                OnError(ApStatusEnum.NoCapture);
            }
            if (resp.HasFlag(ApStatusEnum.CardOverlapped))
            {
                OnError(ApStatusEnum.CardOverlapped);
            }
            if (resp.HasFlag(ApStatusEnum.CardJam))
            {
                OnError(ApStatusEnum.CardJam);
            }
        }

        private void OnError(ApStatusEnum status)
        {
            _log.Error(status.ToString());
            Status = CardDispenserStatus.Error;
            if (!_errorsFlags.Contains(status))
            {
                _errorsFlags.Add(status);
                MessageHasCome?.Invoke(this, new MessageHasComeEventArgs(MessageType.Error, status.ToString()));
            }
        }

        public void SendAndLog(Command command)
        {
            _log.Trace(command.ToString());
            Send(command);
        }

        public void Send(Command command)
        {
            _port.Write(command.Bytes, 0, command.Bytes.Length);
        }

        public void Abort()
        {
            _isAborted = true;
        }

        public async Task Start(CardDispenserSettings settings)
        {
            _settings = settings;
            await Reset();
            await GetStatus();
            //ProhibitCart();
            if(IsEmpty)
            {
                OnError(ApStatusEnum.CardEmptySensorStatus);
            }
            else
            {
                Status = CardDispenserStatus.Idle;
            }
        }

        public async Task Reset()
        {
            SendAndLog(new ResetMessage());
            await Task.Delay(1000);
        }

        public async Task<bool> SpitCard()
        {
            await GetStatus();
            if (CardAtReadingPosition)
            {
                SendAndLog(new DispenseCartToSpitPosCommand());
            }
            return true;
        }

        private async Task GetStatus()
        {
            _isWaitingCardEmptySensorStatus = true;
            SendAndLog(new HighClassStatusCheckingCommand());
            var checker = new TimeoutChecker(_settings.ResponseTimeout);
            while (_isWaitingCardEmptySensorStatus)
            {
                if (checker.Check())
                    throw new TimeoutException("CardDispenser does not respond!");

                await Task.Delay(100);
            }
        }

        public async void MainLoop()
        {
            while (true)
            {
                try
                {
                    await HandleStateMachine();
                    await Task.Delay(200);
                }
                catch (Exception e)
                {
                    _log.Error(e);
                    await Task.Delay(5000);
                }
            }
        }

        private async Task HandleStateMachine()
        {
            if (State == State.NoConnect)
            {
                Connect();
                State = State.WaitingCommand;
            }
            else if (State == State.WaitingCommand)
            {
                if (Command == null)
                    Command = new HighClassStatusCheckingCommand();

                SendAndLog(Command);
                State = State.WaitingAsk;
            }
            else if (State == State.WaitingAsk)
            {
                var isAsk = ReadAsk();
                if (isAsk)
                {
                    WriteEnq();
                    await Task.Delay(50);
                    if (Command is HighClassStatusCheckingCommand)
                    {
                        State = State.WaitingStatus;
                        return;
                    }
                }

                Command = null;
                State = State.WaitingCommand;
            }
            else if (State == State.WaitingStatus)
            {
                var isStatus = ReadStatus();
                if (isStatus)
                {
                    await HandleCommands();
                    State = State.WaitingCommand;
                }
            }
        }

        private async Task HandleCommands()
        {
            if (Status == CardDispenserStatus.TakeCardWaiting)
            {
                if ((DateTime.Now - _startTakeCardTime).TotalMilliseconds > 15000)
                {
                    _startTakeCardTime = DateTime.MaxValue;
                    await CaptureToError();
                }
            }
            else if (Status == CardDispenserStatus.DispensingToExit)
            {
                await ProhibitCapture();
            }
        }

        private async Task CaptureToError()
        {
            SendAndLog(new ErrorCapturePolicyCommand());
            await Task.Delay(100);
            _port.ReadByte();
            WriteEnq();
            await Task.Delay(100);
            SendAndLog(new CaptureCardCommand());
            await Task.Delay(100);
            _port.ReadByte();
            WriteEnq();
            await ProhibitCapture();
        }

        private async Task ProhibitCapture()
        {
            SendAndLog(new ProhibitCapturePolicyCommand());
            await Task.Delay(100);
            _port.ReadByte();
            WriteEnq();
            await Task.Delay(100);
            Status = CardDispenserStatus.Idle;
            _taskCompletionSource.SetResult(true);
        }

        private static void WriteEnq()
        {
            _port.Write(new[] {Command.Enq}, 0, 1);
        }

        private bool ReadStatus()
        {
            var bytesToRead = _port.BytesToRead;
            if (bytesToRead == 9)
            {
                var message = _port.ReadExisting();
                HandleResponse(message);
                return true;
            }

            return false;
        }


        private bool ReadAsk()
        {
            var bytesToRead = _port.BytesToRead;
            if (bytesToRead > 0)
            {
                var v = _port.ReadChar();
                if (v == 6)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
