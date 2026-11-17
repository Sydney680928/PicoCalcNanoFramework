// Copyright 2026 Stéphane Sibué
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

// SD card demo (package PicoCalcNano.SdCard): initialises the card, mounts the FAT file system, lists the root
// folder, writes a small file, reads it back and deletes it (the card is left as it was).

using PicoCalcNano;

namespace PicoCalcDemo
{
    public static class DemoSd
    {
        private const string FileName = "DEMO.TXT";

        public static void Run()
        {
            Ui ui = new Ui();
            PicoCalcSdCard sd = new PicoCalcSdCard();

            try
            {
                ui.PrintLine("SD card demo");
                ui.PrintLine("");

                if (!sd.Init())
                {
                    ui.PrintLine("No card: " + sd.ErrorMessage);
                    ui.PrintLine("Any key: back to the menu");
                    ui.ReadKey();
                    return;
                }

                ui.PrintLine("Card: " + CardTypeName(sd.CardType) + ", " + (sd.CapacityBytes / (1024 * 1024)) + " MB");

                PicoCalcFat fat = new PicoCalcFat(sd);
                if (!fat.Mount())
                {
                    ui.PrintLine("Mount failed: " + fat.ErrorMessage);
                    ui.PrintLine("Any key: back to the menu");
                    ui.ReadKey();
                    return;
                }

                ui.PrintLine(fat.FatType + ", cluster " + fat.ClusterSizeBytes + " bytes");
                ui.PrintLine("");

                // List the root folder (the first entries only)
                PicoCalcFatEntry[] entries = fat.ListDirectory("/");
                if (entries == null)
                {
                    ui.PrintLine("List failed: " + fat.ErrorMessage);
                }
                else
                {
                    ui.PrintLine("Root folder: " + entries.Length + " entries");
                    for (int i = 0; i < entries.Length && i < 8; i++)
                    {
                        PicoCalcFatEntry entry = entries[i];
                        ui.PrintLine("  " + entry.Name + (entry.IsDirectory ? "  <DIR>" : "  " + entry.Size + " bytes"));
                    }

                    if (entries.Length > 8)
                    {
                        ui.PrintLine("  ...");
                    }
                }

                ui.PrintLine("");

                // Write, read back, delete
                if (!fat.WriteAllText(FileName, "Hello from PicoCalcNano.SdCard!"))
                {
                    ui.PrintLine("Write failed: " + fat.ErrorMessage);
                }
                else
                {
                    ui.PrintLine("Wrote " + FileName);
                    ui.PrintLine("Read back: " + fat.ReadAllText(FileName));

                    if (fat.DeleteFile(FileName))
                    {
                        ui.PrintLine("Deleted " + FileName);
                    }
                    else
                    {
                        ui.PrintLine("Delete failed: " + fat.ErrorMessage);
                    }
                }

                ui.PrintLine("");
                ui.PrintLine("Any key: back to the menu");
                ui.ReadKey();
            }
            finally
            {
                sd.Dispose();
                ui.Dispose();
            }
        }

        private static string CardTypeName(PicoCalcSdType type)
        {
            if (type == PicoCalcSdType.SdHc)
            {
                return "SDHC";
            }

            if (type == PicoCalcSdType.SdV2)
            {
                return "SD v2";
            }

            if (type == PicoCalcSdType.SdV1)
            {
                return "SD v1";
            }

            return "unknown";
        }
    }
}
