using PicoCalcNano;
using System;
using System.Device.I2c;
using System.Diagnostics;
using System.Threading;

namespace PicoCalcAnalyseKeyboard
{
    public class Program
    {
        public static void Main()
        {
            var kb = new PicoCalcKeyboard();
            kb.KeyDown += OnKeyDown;
            kb.Start();

            Debug.WriteLine($"Bus={kb.BusId}");

            Thread.Sleep(Timeout.Infinite);
        }

        private static void OnKeyDown(object sender, PicoCalcKeyEventArgs e)
        {
            Debug.WriteLine("code=0x" + e.Code.ToString("X2") + " char=" + (e.HasChar ? e.Char.ToString() : "-") + (e.IsRepeat ? " (repeat)" : ""));
        }
    }
}
