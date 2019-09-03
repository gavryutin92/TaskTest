using System;

namespace Dispenser.Events
{
    public enum MessageType
    {
        Warn,
        Error
    }

    public class MessageHasComeEventArgs : EventArgs
    {
        public MessageType Type { get; set; }
        public string Message { get; set; }

        public MessageHasComeEventArgs(MessageType type, string message)
        {
            Type = type;
            Message = message;
        }
    }
}
