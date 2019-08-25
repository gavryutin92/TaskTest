using System;
using System.Threading;
using System.Threading.Tasks;
using CardDispenserServiceNs;

namespace TaskTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var disp = new CardDispenserService();
            Task.Run(() => disp.MainLoop());
            //var res = await disp.CaptureCardToRead();
            //var res = await disp.DispenseCardToExit();
            //var res = await disp.DispenseCardToRead();
            var res = await disp.GetCardEmptySensorStatus();
            Console.ReadLine();
        }
    }
}
