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

// Sound demo (package PicoCalcNano.Sound): a beep, a scale, a melody and sound effects on the left, right or both speakers.
// Needs a firmware CLR 1.18.0.96 or later, otherwise the notes are not at the right pitch.

using System.Threading;
using PicoCalcNano;

namespace PicoCalcDemo
{
    public static class DemoSound
    {
        private const string Scale = "C5:250 D5:250 E5:250 F5:250 G5:250 A5:250 B5:250 C6:500";

        // "Au clair de la lune" (traditional French song, public domain): quarter note = 300 ms
        private const string AuClairDeLaLune =
            "C5:300 C5:300 C5:300 D5:300 E5:600 D5:600 C5:300 E5:300 D5:300 D5:300 C5:1200 R:300 " +
            "C5:300 C5:300 C5:300 D5:300 E5:600 D5:600 C5:300 E5:300 D5:300 D5:300 C5:1200 R:300 " +
            "D5:300 D5:300 D5:300 D5:300 A4:600 A4:600 D5:300 C5:300 B4:300 A4:300 G4:1200 R:300 " +
            "C5:300 C5:300 C5:300 D5:300 E5:600 D5:600 C5:300 E5:300 D5:300 D5:300 C5:1200";

        // Notes and effects mixed in one melody string (S = sweep, NF = noise with fade-out)
        private const string Jingle = "G4:120 C5:120 E5:120 G5:120 S:600>2400:250 R:100 C6:200 NF:100-3000:500";

        public static void Run()
        {
            Ui ui = new Ui();
            PicoCalcSound sound = new PicoCalcSound();

            try
            {
                ui.PrintLine("Sound demo");
                ui.PrintLine("");
                ui.PrintLine("1  Beep");
                ui.PrintLine("2  Scale");
                ui.PrintLine("3  Au clair de la lune");
                ui.PrintLine("4  Change speaker");
                ui.PrintLine("5  Sound effects");
                ui.PrintLine("6  Notes + effects jingle");
                ui.PrintLine("Esc  Back to the menu");
                ui.PrintLine("");
                ui.PrintLine("Speaker: " + SpeakerName(sound.Speaker));

                while (true)
                {
                    PicoCalcKeyEventArgs e = ui.ReadKey();
                    if (e.Code == PicoCalcKeys.Esc)
                    {
                        break;
                    }

                    if (!e.HasChar)
                    {
                        continue;
                    }

                    if (e.Char == '1')
                    {
                        sound.Beep();
                    }
                    else if (e.Char == '2')
                    {
                        PlayAndWait(ui, sound, Scale);
                    }
                    else if (e.Char == '3')
                    {
                        PlayAndWait(ui, sound, AuClairDeLaLune);
                    }
                    else if (e.Char == '4')
                    {
                        if (sound.Speaker == PicoCalcSpeaker.Left)
                        {
                            sound.Speaker = PicoCalcSpeaker.Right;
                        }
                        else if (sound.Speaker == PicoCalcSpeaker.Right)
                        {
                            sound.Speaker = PicoCalcSpeaker.Both;
                        }
                        else
                        {
                            sound.Speaker = PicoCalcSpeaker.Left;
                        }

                        ui.PrintLine("Speaker: " + SpeakerName(sound.Speaker));
                    }
                    else if (e.Char == '5')
                    {
                        PlayEffects(ui, sound);
                    }
                    else if (e.Char == '6')
                    {
                        PlayAndWait(ui, sound, Jingle);
                    }
                }
            }
            finally
            {
                sound.Dispose();
                ui.Dispose();
            }
        }

        // Plays a melody in the background; a key press stops it (the key is kept for the main loop)
        private static void PlayAndWait(Ui ui, PicoCalcSound sound, string melody)
        {
            ui.ClearKeys();
            sound.PlayAsync(melody);

            while (sound.IsPlaying && !ui.KeyAvailable)
            {
                Thread.Sleep(20);
            }

            if (sound.IsPlaying)
            {
                sound.Stop();
            }
        }

        // Plays every ready-made effect in turn, with its name; a key press stops the sequence
        private static void PlayEffects(Ui ui, PicoCalcSound sound)
        {
            PicoCalcEffect[] effects = new PicoCalcEffect[]
            {
                PicoCalcEffect.Laser, PicoCalcEffect.Jump, PicoCalcEffect.Coin, PicoCalcEffect.Siren,
                PicoCalcEffect.Explosion, PicoCalcEffect.Wind, PicoCalcEffect.Fall, PicoCalcEffect.Tick
            };

            // Names written by hand: Enum.ToString() is not reliable on nanoFramework
            string[] names = new string[]
            {
                "Laser", "Jump", "Coin", "Siren", "Explosion", "Wind", "Fall", "Tick"
            };

            ui.ClearKeys();

            for (int i = 0; i < effects.Length; i++)
            {
                ui.PrintLine(names[i]);
                PlayAndWait(ui, sound, PicoCalcSound.EffectMelody(effects[i]));

                if (ui.KeyAvailable)
                {
                    ui.ClearKeys();
                    return;
                }

                Thread.Sleep(400);
            }
        }

        private static string SpeakerName(PicoCalcSpeaker speaker)
        {
            if (speaker == PicoCalcSpeaker.Left)
            {
                return "left";
            }

            if (speaker == PicoCalcSpeaker.Right)
            {
                return "right";
            }

            return "both";
        }
    }
}
