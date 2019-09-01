using System.Text;

namespace CardDispenserServiceNs.Protocol
{
    public class Command
    {
        public static byte Enq = 5;

        protected static byte stx = 2;
        protected static byte etx = 3;

        protected byte ch0;
        protected byte ch1;
        protected byte bcc;

        public byte[] Bytes
        {
            get
            {
                ComputeBcc();
                return ComputeMessage();
            }
        }

        protected Command(char ch0, char ch1)
        {
            this.ch0 = (byte)ch0;
            this.ch1 = (byte)ch1;
        }

        protected virtual void ComputeBcc()
        {
            bcc = (byte)(stx ^ ch0 ^ ch1 ^ etx);
        }

        protected virtual byte[] ComputeMessage()
        {
            return new byte[] { stx, ch0, ch1, etx, bcc };
        }

        public override string ToString()
        {
            return $"{GetType().Name} {Encoding.ASCII.GetString(Bytes)}";
        }
    }

    public class DispenseCardToExitMessage : Command
    {
        public DispenseCardToExitMessage() : base('D','C') { }
    }

    /// <summary>
    /// Захватить карту. Место назначения определяется командами CapturePolicy
    /// </summary>
    public class CaptureCardCommand : Command
    {
        public CaptureCardCommand() : base('C','P') { }
    }

    public class CheckStatusCommand : Command
    {
        public CheckStatusCommand() : base('R', 'F') { }
    }

    public class HighClassStatusCheckingCommand : Command
    {
        public HighClassStatusCheckingCommand() : base('A', 'P') { }
    }

    public class ResetCommand : Command
    {
        public ResetCommand() : base('R', 'S') { }
    }

    public class CartToPositionCommand : Command
    {
        protected byte position;
        protected CartToPositionCommand(char ch0, char ch1, int position) : base(ch0, ch1)
        {
            this.position = (byte)position;
        }

        protected override void ComputeBcc()
        {
            bcc = (byte)(stx ^ ch0 ^ ch1 ^ position ^ etx);
        }

        protected override byte[] ComputeMessage()
        {
            return new byte[] { stx, ch0, ch1, position, etx, bcc };
        }
    }

    public class DispenseCartToPositionCommand : CartToPositionCommand
    {
        public DispenseCartToPositionCommand(int position) : base('F', 'C', position) { }
    }

    public class CaptureCartToPositionCommand : CartToPositionCommand
    {
        public CaptureCartToPositionCommand(int position) : base('I', 'N', position) { }
    }

    /// <summary>
    /// При вставке карта остановится на ридере
    /// </summary>
    public class ReadCapturePolicyCommand : CaptureCartToPositionCommand
    {
        public ReadCapturePolicyCommand() : base(0x32) { }
    }

    /// <summary>
    /// Запретить прием карт
    /// </summary>
    public class ProhibitCapturePolicyCommand : CaptureCartToPositionCommand
    {
        public ProhibitCapturePolicyCommand() : base(0x30) { }
    }

    /// <summary>
    /// При вставке карта отправляется в сброс
    /// </summary>
    public class ErrorCapturePolicyCommand : CaptureCartToPositionCommand
    {
        public ErrorCapturePolicyCommand() : base(0x31) { }
    }

    /// <summary>
    /// Выдвинуть карту на позицию ридера
    /// </summary>
    public class DispenseCardToReadPosCommand : DispenseCartToPositionCommand
    {
        public DispenseCardToReadPosCommand() : base(0x33) { }
    }

    /// <summary>
    /// Выдвинуть карту на выход
    /// </summary>
    public class DispenseCardToExitPosCommand : DispenseCartToPositionCommand
    {
        public DispenseCardToExitPosCommand() : base(0x34) { }
    }

    /// <summary>
    /// Выплюнуть карту
    /// </summary>
    public class DispenseCartToSpitPosCommand : DispenseCartToPositionCommand
    {
        public DispenseCartToSpitPosCommand() : base(0x30) { }
    }
}
