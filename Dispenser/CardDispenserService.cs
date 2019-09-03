using Dispenser.Events;
using Dispenser.Protocol;
using NLog;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace Dispenser
{
    public enum State
    {
        NoConnect,
        WaitingAsk,
        WaitingCommand,
        WaitingStatus
    }

    public class CardDispenserService : ICardDispenserService
    {
        private Command _command;
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private static SerialPort _port;
        private bool _isException;
        private CardDispenserSettings _settings;
        private TaskCompletionSource<bool> _taskCompletionSource;
        private DateTime _startTakeCardTime = DateTime.MaxValue;
        private readonly List<ApStatusEnum> _errorsFlags = new List<ApStatusEnum>();

        public event EventHandler<MessageHasComeEventArgs> MessageHasCome;
        public event Action<object> ExceptionHasOut;
        public event Action<object, Exception> ExceptionHasCome;

        public bool CardAtReadingPosition { get; private set; }
        public Command Command
        {
            get => _command;
            private set => Interlocked.Exchange(ref _command, value);
        }

        public State State { get; set; }
        public CardDispenserStatus Status { get; set; }
        public void Start(CardDispenserSettings settings)
        {
            _settings = settings;
            Task.Run(() => MainLoop());
        }

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
            _isException = true;
            ExceptionHasCome?.Invoke(this, e);
            State = State.NoConnect;
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
            Status = CardDispenserStatus.Idle;
            _taskCompletionSource.SetResult(false);
        }

        public Task<bool> SpitCard()
        {
            if (Status != CardDispenserStatus.Idle)
                return Task.FromResult(false);

            if(!CardAtReadingPosition)
                return Task.FromResult(true);

            _taskCompletionSource = new TaskCompletionSource<bool>();
            Command = new DispenseCartToSpitPosCommand();
            Status = CardDispenserStatus.SpitCardWaiting;
            return _taskCompletionSource.Task;
        }

        private void Connect()
        {
            _port?.Dispose();

            _port = new SerialPort(_settings.Name);
            _port.BaudRate = _settings.BaudRate;
            _port.Parity = _settings.Parity;
            _port.DataBits = _settings.DataBits;
            _port.StopBits = _settings.StopBits;
            _port.ReadTimeout = _settings.ReadWriteTimeout;
            _port.WriteTimeout = _settings.ReadWriteTimeout;

            //_port = new SerialPort("COM8");
            //_port.BaudRate = 9600;
            //_port.Parity = Parity.None;
            //_port.DataBits = 8;
            //_port.StopBits = StopBits.One;
            //_port.ReadTimeout = 500;
            //_port.WriteTimeout = 500;

            _port.Open();
        }

        private async Task HandleSensor1Status(ApStatusEnum resp)
        {
            if (resp.HasFlag(ApStatusEnum.CaptSensor1Status))
            {
                CardAtReadingPosition = true;
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
                    if ((DateTime.Now - _startTakeCardTime).TotalMilliseconds > _settings.WaitingTakeTime)
                    {
                        _startTakeCardTime = DateTime.MaxValue;
                        await CaptureToError();
                        Status = CardDispenserStatus.Idle;
                        _taskCompletionSource.SetResult(true);
                    }
                }
            }
            else
            {
                CardAtReadingPosition = false;
                if (Status == CardDispenserStatus.TakeCardWaiting)
                {
                    _startTakeCardTime = DateTime.MaxValue;
                    await ProhibitCapture();
                    Status = CardDispenserStatus.Idle;
                    _taskCompletionSource.SetResult(true);
                }
                else if(Status == CardDispenserStatus.SpitCardWaiting)
                {
                    await ProhibitCapture();
                    Status = CardDispenserStatus.Idle;
                    _taskCompletionSource.SetResult(true);
                }
            }
        }

        private async Task HandleResponse(string mes)
        {
            var resp = ApResponse.Parse(mes);
            await HandleErrors(resp);
            await HandleWarnings(resp);
            await HandleSensor1Status(resp);
            HandleStatusWaiting();
        }

        private void HandleStatusWaiting()
        {
            if (Status == CardDispenserStatus.DispenserStatusWaiting)
            {
                Status = CardDispenserStatus.Idle;
                _taskCompletionSource.SetResult(true);
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

        private async Task HandleErrors(ApStatusEnum resp)
        {
            await HandleErrorFlag(resp, ApStatusEnum.FailureAlarmSensorInvalid);
            await HandleErrorFlag(resp, ApStatusEnum.ErrorCardBinIsFull);
            await HandleErrorFlag(resp, ApStatusEnum.CardEmptySensorStatus);
            await HandleErrorFlag(resp, ApStatusEnum.CardDispenseError);
            await HandleErrorFlag(resp, ApStatusEnum.NoCapture);
            await HandleErrorFlag(resp, ApStatusEnum.CardOverlapped);
            await HandleErrorFlag(resp, ApStatusEnum.CardJam);
        }

        private async Task HandleErrorFlag(ApStatusEnum resp, ApStatusEnum flag)
        {
            if (resp.HasFlag(flag))
            {
                await OnError(flag);
            }
            else
            {
                RemoveError(flag);
            }
        }

        private void RemoveError(ApStatusEnum status)
        {
            _errorsFlags.Remove(status);
        }

        private async Task OnError(ApStatusEnum status)
        {
            await Reset();
            if (_settings.IsFatal)
            {
                _log.Error($"DISPENSER ERROR FLAG: {status}");
                if (!_errorsFlags.Contains(status))
                {
                    _errorsFlags.Add(status);
                    MessageHasCome?.Invoke(this, new MessageHasComeEventArgs(MessageType.Error, status.ToString()));
                }
            }
            else
            {
                OnExceptionHasCome(new Exception($"DISPENSER ERROR FLAG: {status}"));
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
                    OnExceptionHasCome(e);
                    await Task.Delay(5000);
                }
            }
        }

        private async Task HandleStateMachine()
        {
            if (State == State.NoConnect)
            {
                Connect();
                OnExceptionHasOut();
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
            _errorsFlags.Clear();
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
