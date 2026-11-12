// Demo : une petite console (REPL) sur le PicoCalc, avec un ecran de test des fonctions de dessin.
// Commandes : help, clear, echo <texte>, calc <a> <op> <b>, lines <n>, graph, bye
//
// Fichiers necessaires dans le projet : PicoCalcConsole.cs, PicoCalcTextScreen.cs, PicoCalcDisplay.cs,
// PicoCalcFont.cs, PicoCalcKeyboard.cs, PicoCalcCanvas.cs

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
                    console.WriteLine("graph   (test du dessin)");
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
                else if (line == "graph")
                {
                    Graph(console);
                }
                else if (line.StartsWith("echo "))
                {
                    console.WriteLine(line.Substring(5));
                }
                else if (line.StartsWith("lines "))
                {
                    try
                    {
                        int n = int.Parse(line.Substring(6));
                        for (int i = 1; i <= n; i++)
                        {
                            console.WriteLine("Ligne " + i);
                        }
                    }
                    catch (Exception)
                    {
                        console.WriteLine("Nombre invalide");
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

        // Ecran de test du dessin : formes directes + canevas en memoire.
        // Reste affiche jusqu'a ce qu'on tape sur une touche, puis la console revient telle qu'elle etait.
        private static void Graph(PicoCalcConsole console)
        {
            PicoCalcDisplay display = console.Display;

            console.BeginGraphics();
            display.FillScreen(PicoCalcColors.Black);

            // --- 1. Formes directes sur l'ecran ---
            display.DrawRect(0, 0, 320, 320, PicoCalcColors.White);           // cadre de l'ecran
            display.FillRect(10, 10, 60, 30, PicoCalcColors.Red);
            display.FillRect(80, 10, 60, 30, PicoCalcColors.Green);
            display.FillRect(150, 10, 60, 30, PicoCalcColors.Blue);
            display.HLine(10, 50, 200, PicoCalcColors.Yellow);
            display.VLine(220, 10, 40, PicoCalcColors.Cyan);

            display.DrawText(10, 60, "Texte x1", PicoCalcColors.White, PicoCalcColors.Black, 1);
            display.DrawText(10, 80, "Texte x2", PicoCalcColors.Yellow, PicoCalcColors.Black, 2);
            display.DrawText(10, 115, "x3", PicoCalcColors.Cyan, PicoCalcColors.Black, 3);

            // --- 2. Canevas en memoire : 140 x 140 ---
            PicoCalcCanvas canvas = new PicoCalcCanvas(140, 140);
            canvas.Clear(0x000040);                                            // bleu nuit
            canvas.DrawRect(0, 0, 140, 140, PicoCalcColors.White);
            canvas.DrawLine(0, 0, 139, 139, PicoCalcColors.Red);
            canvas.DrawLine(139, 0, 0, 139, PicoCalcColors.Green);
            canvas.DrawCircle(70, 70, 60, PicoCalcColors.Yellow);
            canvas.FillCircle(70, 70, 25, PicoCalcColors.Magenta);
            canvas.FillTriangle(20, 120, 50, 80, 80, 120, PicoCalcColors.Cyan);
            canvas.DrawTriangle(90, 20, 130, 20, 110, 50, PicoCalcColors.White);
            canvas.DrawText(8, 6, "Canvas", PicoCalcColors.White, 1);

            // Un petit sprite avec fond transparent (magenta pur = transparent)
            PicoCalcCanvas sprite = new PicoCalcCanvas(32, 32);
            sprite.Clear(PicoCalcColors.Magenta);
            sprite.FillCircle(16, 16, 14, PicoCalcColors.Yellow);
            sprite.FillCircle(11, 12, 2, PicoCalcColors.Black);
            sprite.FillCircle(21, 12, 2, PicoCalcColors.Black);
            sprite.DrawLine(9, 21, 15, 24, PicoCalcColors.Black);
            sprite.DrawLine(15, 24, 23, 21, PicoCalcColors.Black);
            canvas.DrawCanvas(sprite, 100, 95, PicoCalcColors.Magenta);

            canvas.DrawTo(display, 90, 160);

            display.DrawText(10, 302, "Une touche pour revenir", PicoCalcColors.White, PicoCalcColors.Black, 1);

            // On attend une touche (on oublie d'abord celles deja tapees, comme le Entree de la commande)
            console.ClearKeys();
            console.ReadKey();

            console.EndGraphics();
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
