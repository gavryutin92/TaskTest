using System;
using System.Threading;
using System.Threading.Tasks;
using Dispenser;

namespace TaskTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var disp = new CardDispenserService();
            Task.Run(() => disp.MainLoop());
            Task.Run( async () => { await Task.Delay(5000); disp.CancelCapture();});
            var res = await disp.CaptureCardToRead();
            //var res = await disp.DispenseCardToExit();
            //var res = await disp.DispenseCardToRead();
            //await disp.GetStatus();
            //var res = await disp.SpitCard();
            Console.ReadLine();
        }
    }
}