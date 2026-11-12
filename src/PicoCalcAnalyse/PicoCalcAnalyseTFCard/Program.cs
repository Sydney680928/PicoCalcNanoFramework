// TEST FAT 3 : suppression de fichiers, creation / suppression de dossiers, renommage.
// A faire sur la carte de 1 Go (FAT16) PUIS sur celle de 64 Go (FAT32).
// Le test ne touche que ses propres fichiers et les fichiers crees par le test d'ecriture precedent
// (ECRIT.TXT, BIG.TXT, TEST/ECRIT2.TXT, qui sont supprimes). HELLO.TXT et TEST/NOTE.TXT ne sont pas touches.
// Remplace Program.cs du projet SD par ce fichier. PicoCalcSdCard.cs et PicoCalcFat.cs (nouvelles versions).
// A la fin il reste sur la carte : DOSS/AA.TXT et DOSS/SOUS/C.TXT (pour verifier sur l'ordinateur).

using System;
using System.Diagnostics;
using System.Threading;

namespace PicoCalc
{
    public class Program
    {
        private static int _failures = 0;

        public static void Main()
        {
            Thread.Sleep(3000);

            Debug.WriteLine("TEST FAT 3 (suppression, dossiers, renommage)");

            PicoCalcSdCard sd = new PicoCalcSdCard();
            if (!sd.Init())
            {
                Debug.WriteLine("Init carte ECHEC : " + sd.ErrorMessage);
                Thread.Sleep(Timeout.Infinite);
            }

            PicoCalcFat fat = new PicoCalcFat(sd);
            if (!fat.Mount())
            {
                Debug.WriteLine("Mount ECHEC : " + fat.ErrorMessage);
                Thread.Sleep(Timeout.Infinite);
            }

            Debug.WriteLine("Mount OK : " + fat.FatType);
            fat.SetTimestamp(2026, 11, 11, 23, 59, 0);

            // --- A. Nettoyage des fichiers du test precedent (absents = pas grave)
            Debug.WriteLine("--- A. Nettoyage");
            Try(fat, "suppression ECRIT.TXT", fat.DeleteFile("ECRIT.TXT"));
            Try(fat, "suppression BIG.TXT", fat.DeleteFile("BIG.TXT"));
            Try(fat, "suppression BIG.BIN", fat.DeleteFile("BIG.BIN"));
            Try(fat, "suppression TEST/ECRIT2.TXT", fat.DeleteFile("TEST/ECRIT2.TXT"));

            // --- B. Un dossier temporaire cree puis supprime
            Debug.WriteLine("--- B. Dossier temporaire TMP");
            Expect(fat, "creation du dossier TMP", fat.CreateDirectory("TMP"));
            Expect(fat, "ecriture TMP/X.TXT", fat.WriteAllText("TMP/X.TXT", "x"));
            ExpectFail(fat, "suppression de TMP non vide (doit etre refusee)", fat.DeleteDirectory("TMP"));
            Expect(fat, "suppression TMP/X.TXT", fat.DeleteFile("TMP/X.TXT"));
            Expect(fat, "suppression du dossier TMP", fat.DeleteDirectory("TMP"));
            ExpectFail(fat, "TMP n'existe plus", fat.ListDirectory("TMP") != null);

            // --- C. Un dossier qu'on garde : DOSS, avec un sous-dossier
            Debug.WriteLine("--- C. Dossier DOSS");
            Expect(fat, "creation du dossier DOSS", fat.CreateDirectory("DOSS"));
            ExpectFail(fat, "creation de DOSS une 2e fois (doit etre refusee)", fat.CreateDirectory("DOSS"));
            Expect(fat, "ecriture DOSS/A.TXT", fat.WriteAllText("DOSS/A.TXT", "Fichier A"));
            Expect(fat, "ecriture DOSS/B.TXT", fat.WriteAllText("DOSS/B.TXT", "Fichier B"));
            Expect(fat, "creation du dossier DOSS/SOUS", fat.CreateDirectory("DOSS/SOUS"));
            Expect(fat, "ecriture DOSS/SOUS/C.TXT", fat.WriteAllText("DOSS/SOUS/C.TXT", "Fichier C dans SOUS"));

            // --- D. Renommage
            Debug.WriteLine("--- D. Renommage");
            Expect(fat, "renommer DOSS/A.TXT en AA.TXT", fat.Rename("DOSS/A.TXT", "AA.TXT"));
            Check(fat, "DOSS/AA.TXT contient 'Fichier A'", fat.ReadAllText("DOSS/AA.TXT") == "Fichier A");
            ExpectFail(fat, "DOSS/A.TXT n'existe plus", fat.ReadAllText("DOSS/A.TXT") != null);
            ExpectFail(fat, "renommer vers un nom deja pris (doit etre refuse)", fat.Rename("DOSS/AA.TXT", "B.TXT"));

            // --- E. Suppression dans DOSS
            Debug.WriteLine("--- E. Suppression");
            Expect(fat, "suppression DOSS/B.TXT", fat.DeleteFile("DOSS/B.TXT"));
            ExpectFail(fat, "DOSS/B.TXT n'existe plus", fat.ReadAllText("DOSS/B.TXT") != null);
            Check(fat, "DOSS/SOUS/C.TXT toujours lisible", fat.ReadAllText("DOSS/SOUS/C.TXT") == "Fichier C dans SOUS");

            // --- F. Reutilisation de l'espace libere
            Debug.WriteLine("--- F. Reecriture apres suppression");
            Expect(fat, "ecriture DOSS/D.TXT", fat.WriteAllText("DOSS/D.TXT", "Fichier D"));
            Check(fat, "DOSS/D.TXT relu", fat.ReadAllText("DOSS/D.TXT") == "Fichier D");
            Expect(fat, "suppression DOSS/D.TXT", fat.DeleteFile("DOSS/D.TXT"));

            // --- Listes finales
            ListDir(fat, "/");
            ListDir(fat, "DOSS");
            ListDir(fat, "DOSS/SOUS");

            if (_failures == 0)
            {
                Debug.WriteLine("RESULTAT : tout est OK");
            }
            else
            {
                Debug.WriteLine("RESULTAT : " + _failures + " echec(s)");
            }

            Debug.WriteLine("Fin du test. Branche la carte sur l'ordinateur et lance une verification du disque.");
            Thread.Sleep(Timeout.Infinite);
        }

        // Doit reussir
        private static void Expect(PicoCalcFat fat, string label, bool ok)
        {
            if (ok)
            {
                Debug.WriteLine("OK    " + label);
            }
            else
            {
                _failures++;
                Debug.WriteLine("ECHEC " + label + " : " + fat.ErrorMessage);
            }
        }

        // Doit echouer (ok == false attendu)
        private static void ExpectFail(PicoCalcFat fat, string label, bool ok)
        {
            if (!ok)
            {
                Debug.WriteLine("OK    " + label + " [refuse : " + fat.ErrorMessage + "]");
            }
            else
            {
                _failures++;
                Debug.WriteLine("ECHEC " + label + " : l'operation a reussi alors qu'elle devait echouer");
            }
        }

        private static void Check(PicoCalcFat fat, string label, bool condition)
        {
            if (condition)
            {
                Debug.WriteLine("OK    " + label);
            }
            else
            {
                _failures++;
                Debug.WriteLine("ECHEC " + label + " : " + fat.ErrorMessage);
            }
        }

        // Pour le nettoyage : on affiche le resultat sans compter d'echec
        private static void Try(PicoCalcFat fat, string label, bool ok)
        {
            if (ok)
            {
                Debug.WriteLine("OK    " + label);
            }
            else
            {
                Debug.WriteLine("--    " + label + " : " + fat.ErrorMessage);
            }
        }

        private static void ListDir(PicoCalcFat fat, string path)
        {
            Debug.WriteLine("--- Contenu de " + path);
            PicoCalcFatEntry[] entries = fat.ListDirectory(path);
            if (entries == null)
            {
                Debug.WriteLine("ERREUR : " + fat.ErrorMessage);
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                PicoCalcFatEntry e = entries[i];
                if (e.IsDirectory)
                {
                    Debug.WriteLine("  [DOSSIER] " + e.Name);
                }
                else
                {
                    Debug.WriteLine("  " + e.Name + "  " + e.Size + " octets");
                }
            }
        }
    }
}
