// Demo : une petite console (REPL) sur le PicoCalc.
// Commandes : help, clear, echo <texte>, calc <a> <op> <b>, lines <n>, bye

using System;
using System.Threading;

namespace PicoCalc
{
    public class Program
    {
        public static void Main()
        {
            // Pause courte pour laisser le debogueur / le deploiement reprendre la main
            Thread.Sleep(3000);

            PicoCalcConsole console = new PicoCalcConsole();
            console.Start();

            console.WriteLine("PicoCalc console");
            console.WriteLine("Tape help pour l'aide.");
            console.WriteLine();

            while (true)
            {
                string line = console.ReadLine("> ");
                if (line == null)
                {
                    continue;   // Break / Ctrl+C
                }

                line = line.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line == "help")
                {
                    console.WriteLine("help  clear  bye");
                    console.WriteLine("echo <texte>");
                    console.WriteLine("calc <a> <op> <b>   (+ - * /)");
                    console.WriteLine("lines <n>   (test defilement)");
                    console.WriteLine("Fleches haut/bas = historique");
                }
                else if (line == "clear")
                {
                    console.Clear();
                }
                else if (line == "bye")
                {
                    console.WriteLine("A bientot !");
                    Thread.Sleep(Timeout.Infinite);
                }
                else if (line.StartsWith("echo "))
                {
                    console.WriteLine(line.Substring(5));
                }
                else if (line.StartsWith("lines "))
                {
                    int n = int.Parse(line.Substring(6));
                    for (int i = 1; i <= n; i++)
                    {
                        console.WriteLine("Ligne " + i);
                    }
                }
                else if (line.StartsWith("calc "))
                {
                    Calc(console, line.Substring(5));
                }
                else
                {
                    console.WriteLine("Commande inconnue : " + line);
                }
            }
        }

        private static void Calc(PicoCalcConsole console, string args)
        {
            string[] parts = args.Split(' ');
            if (parts.Length != 3)
            {
                console.WriteLine("Usage : calc <a> <op> <b>");
                return;
            }

            try
            {
                double a = double.Parse(parts[0]);
                double b = double.Parse(parts[2]);
                string op = parts[1];
                double r;

                if (op == "+") r = a + b;
                else if (op == "-") r = a - b;
                else if (op == "*") r = a * b;
                else if (op == "/") r = a / b;
                else
                {
                    console.WriteLine("Operateur inconnu : " + op);
                    return;
                }

                console.WriteLine("= " + r.ToString());
            }
            catch (Exception)
            {
                console.WriteLine("Nombre invalide");
            }
        }
    }
}
