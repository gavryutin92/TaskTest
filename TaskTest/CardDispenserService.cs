using CardDispenserServiceNs.Protocol;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
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
        private object _lockObj = new object();

        public Command Command
        {
            get => _command;
            private set => Interlocked.Exchange(ref _command, value);
        }

        public State State { get; set; }

        public CardDispenserStatus StatusPrev { get; private set; }

        private CardDispenserStatus _status;
        public CardDispenserStatus Status
        {
            get => _status;
            set
            {
                StatusPrev = _status;
                _status = value;
                StatusChanged?.Invoke(Status);
            }
        }

        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private static SerialPort _port;
        private bool _isConnected;
        private bool _isException;

        private volatile bool _isWaitingFull;
        private volatile bool _isWaitingEmpty;
        private volatile bool _isAborted = false;
        public bool CardAtReadingPosition { get; private set; }

        private TaskCompletionSource<bool> _taskCompletionSource;
        private DateTime _startTakeCardTime = DateTime.MaxValue;

        public event Action<CardDispenserStatus> StatusChanged;
        public event EventHandler<MessageHasComeEventArgs> MessageHasCome;
        public event Action<object> ExceptionHasOut;
        public event Action<object, Exception> ExceptionHasCome;

        private CardDispenserSettings _settings;


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
            if (Status != CardDispenserStatus.Idle)
                return Task.FromResult(false);

            _taskCompletionSource = new TaskCompletionSource<bool>();
            Command = new ReadCapturePolicyCommand();
            Status = CardDispenserStatus.CapturingToRead;
            return _taskCompletionSource.Task;
        }

        public Task<bool> DispenseCardToExit()
        {
            if (Status != CardDispenserStatus.Idle)
                return Task.FromResult(false);

            _startTakeCardTime = DateTime.Now;
            _taskCompletionSource = new TaskCompletionSource<bool>();
            Command = new DispenseCardToExitPosCommand();
            Status = CardDispenserStatus.DispensingToExit;
            return _taskCompletionSource.Task;
        }

        public Task<bool> DispenseCardToRead()
        {
            if (Status != CardDispenserStatus.Idle)
                return Task.FromResult(false);

            _taskCompletionSource = new TaskCompletionSource<bool>();
            Command = new DispenseCardToReadPosCommand();
            Status = CardDispenserStatus.DispensingToRead;
            return _taskCompletionSource.Task;
        }

        public Task<bool> GetStatus()
        {
            if (Status != CardDispenserStatus.Idle)
                return Task.FromResult(false);

            _taskCompletionSource = new TaskCompletionSource<bool>();
            Status = CardDispenserStatus.DispenserStatusWaiting;
            return _taskCompletionSource.Task;
        }

        public void CancelCapture()
        {
            _isWaitingFull = false;
            _taskCompletionSource.SetResult(true);
        }

        public Task<bool> SpitCard()
        {
            _taskCompletionSource = new TaskCompletionSource<bool>();
            _isWaitingEmpty = true;
            Command = new DispenseCartToSpitPosCommand();
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
            _port = new SerialPort("COM8");
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

        private async Task HandleSensor1Status(ApStatusEnum resp)
        {
            if (resp.HasFlag(ApStatusEnum.CaptSensor1Status))
            {
                if (Status == CardDispenserStatus.CapturingToRead   || 
                    Status == CardDispenserStatus.DispensingToRead)
                {
                    Status = CardDispenserStatus.ReadCardWaiting;
                    _taskCompletionSource.SetResult(true);
                }
                else if (Status == CardDispenserStatus.DispensingToExit)
                {
                    _startTakeCardTime = DateTime.Now;
                    Status = CardDispenserStatus.TakeCardWaiting;
                }
                else if(Status == CardDispenserStatus.TakeCardWaiting)
                {
                    if ((DateTime.Now - _startTakeCardTime).TotalMilliseconds > 15000)
                    {
                        _startTakeCardTime = DateTime.MaxValue;
                        Status = CardDispenserStatus.Idle;
                        await CaptureToError();
                        _taskCompletionSource.SetResult(true);
                    }
                }
            }
            else
            {
                if(Status == CardDispenserStatus.TakeCardWaiting)
                {
                    _startTakeCardTime = DateTime.MaxValue;
                    Status = CardDispenserStatus.Idle;
                    await ProhibitCapture();
                    _taskCompletionSource.SetResult(true);
                }
            }
        }

        private async Task HandleResponse(string mes)
        {
            var resp = ApResponse.Parse(mes);
            HandleErrors(resp);
            await HandleWarnings(resp);
            await HandleSensor1Status(resp);
            HandleStatusWaiting();
        }

        private void HandleStatusWaiting()
        {
            if (Status == CardDispenserStatus.DispenserStatusWaiting)
            {
                _taskCompletionSource.SetResult(true);
                Status = CardDispenserStatus.Idle;
            }
        }

        private bool _prevCardPreEmpty;

        private async Task HandleWarnings(ApStatusEnum resp)
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
                if(Status == CardDispenserStatus.CapturingToRead)
                {
                    await Reset();
                    Status = CardDispenserStatus.Idle;
                    _taskCompletionSource.SetResult(false);
                }
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
            if (resp.HasFlag(ApStatusEnum.CardEmptySensorStatus))
            {
                OnError(ApStatusEnum.CardEmptySensorStatus);
            }
            if (resp.HasFlag(ApStatusEnum.CardDispenseError))
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
                    State = State.NoConnect;
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
                if(Command == null)
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
                var isStatus = await ReadStatus();
                if (isStatus)
                {
                    State = State.WaitingCommand;
                }
            }
        }

        private async Task CaptureToError()
        {
            SendAndLog(new ErrorCapturePolicyCommand());
            await Task.Delay(100);
            _port.ReadByte();
            WriteEnq();
            await Task.Delay(1000);
            SendAndLog(new CaptureCardCommand());
            await Task.Delay(100);
            _port.ReadByte();
            WriteEnq();
            await Task.Delay(1000);
            await ProhibitCapture();
        }

        private async Task ProhibitCapture()
        {
            SendAndLog(new ProhibitCapturePolicyCommand());
            await Task.Delay(100);
            _port.ReadByte();
            WriteEnq();
            await Task.Delay(100);
        }

        private async Task Reset()
        {
            SendAndLog(new ResetCommand());
            await Task.Delay(100);
            _port.ReadByte();
            WriteEnq();
            await Task.Delay(1000);
            await ProhibitCapture();
        }

        private static void WriteEnq()
        {
            _port.Write(new[] {Command.Enq}, 0, 1);
        }

        private async Task<bool> ReadStatus()
        {
            var bytesToRead = _port.BytesToRead;
            if (bytesToRead == 9)
            {
                var message = _port.ReadExisting();
                await HandleResponse(message);
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
