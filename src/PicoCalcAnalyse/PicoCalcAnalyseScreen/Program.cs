using PicoCalc;
using System;
using System.Device.Spi;
using System.Diagnostics;
using System.Threading;

namespace PicoCalcAnalyseScreen
{
    public class Program
    {
        public static void Main()
        {
            var display = new PicoCalcDisplay(500000);   // SPI matériel
            display.InvertColors = true;
            display.Init();
            display.FillRect(0, 0, 40, 40, 255, 0, 0);
            while (true) { Thread.Sleep(1000); }

            //Thread.Sleep(Timeout.Infinite);
        }
    }
}
