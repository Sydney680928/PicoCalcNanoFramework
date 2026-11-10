using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;
using System.Diagnostics;
using System.Threading;

namespace PicoCalcAnalyseTFCard
{
    public class Program
    {
        public static void Main()
        {
            ScanReserved("au depart");

            I2cDevice kb = I2cDevice.Create(new I2cConnectionSettings(1, 0x1F));
            ScanReserved("apres I2C bus 1");

            SpiConnectionSettings s = new SpiConnectionSettings(1, -1);
            s.ClockFrequency = 1000000;
            s.Mode = SpiMode.Mode0;
            SpiDevice spi = SpiDevice.Create(s);
            ScanReserved("apres SPI bus 1");

            Debug.WriteLine("fin");

            Thread.Sleep(Timeout.Infinite);

            /*
            
            GpioController gpio = new GpioController();
            GpioPin cs = gpio.OpenPin(17, PinMode.Output);   // CS de la SD
            cs.Write(PinValue.High);

            SpiConnectionSettings s = new SpiConnectionSettings(0, -1);   // bus 0 = SPI0
            s.ClockFrequency = 400000;
            s.Mode = SpiMode.Mode0;
            SpiDevice spi = SpiDevice.Create(s);

            // 80 impulsions d'horloge, CS haut
            spi.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });

            cs.Write(PinValue.Low);
            spi.Write(new byte[] { 0x40, 0x00, 0x00, 0x00, 0x00, 0x95 });   // CMD0

            byte[] ff = new byte[] { 0xFF };
            byte[] rx = new byte[1];
            for (int i = 0; i < 16; i++)
            {
                spi.TransferFullDuplex(ff, rx);
                Debug.WriteLine("octet " + i + " = 0x" + rx[0].ToString("X2"));
                if (rx[0] != 0xFF)
                {
                    break;
                }
            }

            cs.Write(PinValue.High);
            Debug.WriteLine("fin");

            Thread.Sleep(Timeout.Infinite);

            */
        }

        private static GpioController _gpio = new GpioController();
        private static void ScanReserved(string label)
        {
            string reserved = "";
            for (int pin = 0; pin <= 28; pin++)
            {
                if (pin >= 23 && pin <= 25) { continue; }   // pins spéciales du Pico
                try
                {
                    GpioPin p = _gpio.OpenPin(pin);
                    p.Dispose();
                }
                catch
                {
                    reserved += pin + " ";
                }
            }
            Debug.WriteLine(label + " -> reservees : " + reserved);
        }
    }
}
