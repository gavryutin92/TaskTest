using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dispenser
{
    public class CardDispenserSettings
    {
        public string Name { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public Parity Parity { get; set; } = Parity.None;
        public int DataBits { get; set; } = 8;
        public StopBits StopBits { get; set; } = StopBits.One;
        /// <summary>
        /// Время ожидания [мс], устанавливается из CardShopSettings.UserInputTime
        /// </summary>
        public int WaitingTakeTime { get; set; } = 10000;
        /// <summary>
        /// Время [мс] ожидания ответа, если ответ не будет получен - выброс исключения  
        /// </summary>
        public int ResponseTimeout { get; set; } = 2000;
        public int ReadWriteTimeout { get; set; } = 1000;
        public bool IsFatal { get; internal set; }
    }
}
