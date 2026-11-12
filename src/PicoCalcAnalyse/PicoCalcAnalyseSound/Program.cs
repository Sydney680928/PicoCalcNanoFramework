using PicoCalc;
using System;
using System.Device.Pwm;
using System.Diagnostics;
using System.Threading;

namespace PicoCalcAnalyseSound
{
    public class Program
    {
        public static void Main()
        {
            Thread.Sleep(3000);

            PicoCalcSound sound = new PicoCalcSound();
            sound.NoteGap = 1500;

            int[] frequencies = new int[] { 100, 200, 400, 800, 1600, 3200, 6400, 12800 };

            for (int i = 0; i < frequencies.Length; i++)
            {
                Debug.WriteLine("Note " + (i + 1) + " : " + frequencies[i] + " Hz demandes");
                sound.Tone(frequencies[i], 4000);
            }

            Debug.WriteLine("Fin du test");
            Thread.Sleep(Timeout.Infinite);
        }
    }
}
