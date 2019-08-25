using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CardDispenserServiceNs.Protocol
{
    [Flags]
    public enum RfStatusEnum
    {
        DispensingCard     = 0x800,
        CapturingCard      = 0x400,
        CardDispenseError  = 0x200,
        CardCaptureError   = 0x100,
        NoCaptureCard      = 0x80,
        OverlappingCards   = 0x40,
        JammingCard        = 0x20,
        CardPreEmptyStatus = 0x10,
        CardEmptyStatus    = 0x8,
        DispSensorStatus   = 0x4,
        CaptSensor2Status  = 0x2,
        CaptSensor1Status  = 0x1
    }

    [Flags]
    public enum ApStatusEnum
    {
        FailureAlarmSensorInvalid = 0x2000,
        ErrorCardBinIsFull        = 0x1000,
        CardIsDispensing          = 0x800,
        CardIsCapturing           = 0x400,
        CardDispenseError         = 0x200,
        CardCaptureError          = 0x100,
        NoCapture                 = 0x80,
        CardOverlapped            = 0x40,
        CardJam                   = 0x20,
        CardPreEmpty              = 0x10,
        CardEmptySensorStatus     = 0x8,
        DispSensorStatus          = 0x4,
        CaptSensor2Status         = 0x2,
        CaptSensor1Status         = 0x1
    }

    public class Response
    {
        protected const byte stx = 2;
        protected const byte etx = 3;
        protected static byte bcc;
    }

    public class RfResponse : Response
    {
        protected static byte v0;
        protected static byte v1;
        protected static byte v2;

        public static RfStatusEnum Parse(string msg)
        {
            v0 = (byte)msg[2];
            v1 = (byte)msg[3];
            v2 = (byte)msg[4];

            bcc = (byte)(stx ^ 'S' ^ 'F' ^ v0 ^ v1 ^ v2 ^ etx);
            if ((byte)msg[6] != bcc)
                throw new InvalidOperationException($"Wrong RfResponse bcc for {msg}");
            var retStr = msg.Substring(2, 3);
            var ret = int.Parse(retStr, System.Globalization.NumberStyles.HexNumber);
            return (RfStatusEnum)ret;
        }
    }

    public class ApResponse : Response
    {
        public static ApStatusEnum Parse(string msg)
        {
            var v0 = (byte)msg[0];
            var v1 = (byte)msg[1];
            var v2 = (byte)msg[2];
            var v3 = (byte)msg[3];
            var v4 = (byte)msg[4];
            var v5 = (byte)msg[5];
            var v6 = (byte)msg[6];
            var v7 = (byte)msg[7];

            bcc = (byte)(v0 ^ v1 ^ v2 ^ v3 ^ v4 ^ v5 ^ v6 ^ v7);
            var ch = (byte) msg[8];
            if (ch != bcc)
                throw new InvalidOperationException($"Wrong ApResponse bcc for {msg}");
            var retStr = msg.Substring(3, 4);
            var ret = int.Parse(retStr, System.Globalization.NumberStyles.HexNumber);
            return (ApStatusEnum)ret;
        }
    }
}
