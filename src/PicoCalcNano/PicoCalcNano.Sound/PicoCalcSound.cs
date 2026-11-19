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

// PicoCalcSound - sound for the PicoCalc (.NET nanoFramework): beeps, notes, melodies and sound effects.
//
// The PicoCalc has two PWM audio outputs: GP26 (left speaker, verified) and GP27 (right).
// A square wave is produced with PwmChannel; the "volume" is the duty cycle.
//
// Facts learned on real hardware:
//   - Changing the frequency of a running PwmChannel gives no sound: each note therefore creates
//     a NEW channel (created, started, stopped, disposed).
//   - GP28 is NOT an audio output on the PicoCalc.
//
//     var sound = new PicoCalcSound();
//     sound.Beep();                                   // short beep (left speaker by default)
//     sound.Tone(440, 500);                           // A4 for 500 ms
//     sound.Play("C4:250 E4:250 G4:500 R:250 C5:500"); // notes, rests (R), in milliseconds
//     sound.PlayAsync("C4:250 D4:250 E4:250");        // does not block; sound.Stop() cancels
//     sound.PlayEffect(PicoCalcEffect.Laser);         // ready-made sound effect
//     sound.Sweep(2400, 300, 130);                    // glide from 2400 Hz down to 300 Hz in 130 ms
//     sound.Noise(60, 600, 900, true);                // noise (explosion), fading out
//
// Melody format: space-separated tokens "NOTE:milliseconds". NOTE = letter A..G, optional '#' (sharp)
// or 'b' (flat), octave digit (A4 = 440 Hz). "R:200" is a rest. Example: "F#3:300 Bb4:150 R:100".
//
// Effects can be mixed with the notes in a melody:
//     "S:2400>300:130"    sweep (glide) from 2400 Hz to 300 Hz in 130 ms ("SF:" = the volume also fades out)
//     "N:100-3000:400"    noise: random frequencies between 100 and 3000 Hz for 400 ms ("NF:" = fading out)
// Example: "G4:120 C5:120 E5:120 S:600>2400:250 R:100 NF:100-3000:500".
//
// NuGet package: nanoFramework.System.Device.Pwm

using System;
using System.Device.Pwm;
using System.Threading;

namespace PicoCalcNano
{
    public enum PicoCalcSpeaker
    {
        Left,
        Right,
        Both
    }

    /// <summary>Ready-made sound effects, see PicoCalcSound.PlayEffect.</summary>
    public enum PicoCalcEffect
    {
        Laser,
        Jump,
        Coin,
        Siren,
        Explosion,
        Wind,
        Fall,
        Tick
    }

    public class PicoCalcSound : IDisposable
    {
        public const int PinLeft = 26;
        public const int PinRight = 27;

        // Frequencies of octave 4, from C to B
        private static readonly int[] Octave4 = new int[] { 262, 277, 294, 311, 330, 349, 370, 392, 415, 440, 466, 494 };

        private bool _stopRequested;
        private Thread _thread;
        private readonly Random _random = new Random();

        /// <summary>Loudness from 0.0 (silent) to 1.0 (maximum). Default 0.6.</summary>
        public double Volume = 0.6;

        /// <summary>Speaker(s) used by Beep, Tone and Play. Default: left only. Left, Right and Both all
        /// work (GP26 and GP27 are the two outputs of the same PWM slice). Needs a firmware with the PWM
        /// clock divider fix (CLR 1.18.0.96 or later), see docs/sound.md.</summary>
        public PicoCalcSpeaker Speaker = PicoCalcSpeaker.Left;

        /// <summary>Pause in milliseconds added after each note, so repeated notes stay distinct.</summary>
        public int NoteGap = 15;

        /// <summary>Duration in milliseconds of one step of Sweep and Noise (default 5). Each step creates a new
        /// PWM channel, which costs about 0.7 ms: below 2 ms the overhead is too big to be useful.</summary>
        public int StepMs = 5;

        /// <summary>True while PlayAsync is playing.</summary>
        public bool IsPlaying
        {
            get { return _thread != null && _thread.IsAlive; }
        }

        /// <summary>Short beep (1000 Hz, 120 ms).</summary>
        public void Beep()
        {
            Tone(1000, 120);
        }

        /// <summary>Play a note of the given frequency (Hz) for 'milliseconds'. Blocks until it is over.</summary>
        public void Tone(int frequency, int milliseconds)
        {
            if (ToneCore(frequency, milliseconds, Volume) && NoteGap > 0)
            {
                Thread.Sleep(NoteGap);
            }
        }

        // One note at the given volume, without the NoteGap. Returns false if nothing was played (rest or silent).
        private bool ToneCore(int frequency, int milliseconds, double volume)
        {
            if (frequency <= 0 || volume <= 0.0)
            {
                Thread.Sleep(milliseconds);
                return false;
            }

            // Duty cycle 0.5 is the loudest for a square wave
            double duty = volume > 1.0 ? 0.5 : volume * 0.5;

            PwmChannel left = null;
            PwmChannel right = null;

            try
            {
                if (Speaker != PicoCalcSpeaker.Right)
                {
                    left = PwmChannel.CreateFromPin(PinLeft, frequency, duty);
                }

                if (Speaker != PicoCalcSpeaker.Left)
                {
                    right = PwmChannel.CreateFromPin(PinRight, frequency, duty);
                }

                if (left != null) { left.Start(); }
                if (right != null) { right.Start(); }

                Thread.Sleep(milliseconds);
            }
            finally
            {
                if (left != null) { left.Stop(); left.Dispose(); }
                if (right != null) { right.Stop(); right.Dispose(); }
            }

            return true;
        }

        /// <summary>Glide from 'fromHz' to 'toHz' in 'milliseconds' (a laser, a jump, a fall...). Blocks until over.</summary>
        public void Sweep(int fromHz, int toHz, int milliseconds)
        {
            Sweep(fromHz, toHz, milliseconds, false);
        }

        /// <summary>Glide from 'fromHz' to 'toHz' in 'milliseconds'; with 'fadeOut' the volume also goes down to 0.</summary>
        public void Sweep(int fromHz, int toHz, int milliseconds, bool fadeOut)
        {
            _stopRequested = false;
            SweepCore(fromHz, toHz, milliseconds, fadeOut);
        }

        /// <summary>Noise (random frequencies between 'minHz' and 'maxHz') for 'milliseconds'. Blocks until over.</summary>
        public void Noise(int minHz, int maxHz, int milliseconds)
        {
            Noise(minHz, maxHz, milliseconds, false);
        }

        /// <summary>Noise for 'milliseconds'; with 'fadeOut' the volume goes down to 0 (explosion, wind...).</summary>
        public void Noise(int minHz, int maxHz, int milliseconds, bool fadeOut)
        {
            _stopRequested = false;
            NoiseCore(minHz, maxHz, milliseconds, fadeOut);
        }

        // The frequency follows the REAL elapsed time, not the number of steps, so the duration is respected
        // whatever the cost of a step.
        private void SweepCore(int fromHz, int toHz, int milliseconds, bool fadeOut)
        {
            RunSteps(milliseconds, fadeOut, (ratio) => fromHz + (int)((toHz - fromHz) * ratio));
        }

        private void NoiseCore(int minHz, int maxHz, int milliseconds, bool fadeOut)
        {
            if (maxHz < minHz)
            {
                int swap = minHz;
                minHz = maxHz;
                maxHz = swap;
            }

            int low = minHz;
            int range = maxHz - minHz + 1;
            RunSteps(milliseconds, fadeOut, (ratio) => low + _random.Next(range));
        }

        private delegate int FrequencyAt(double ratio);

        // Plays short steps until 'milliseconds' have really elapsed. ratio goes from 0.0 to 1.0.
        private void RunSteps(int milliseconds, bool fadeOut, FrequencyAt frequencyAt)
        {
            int stepMs = StepMs < 2 ? 2 : StepMs;
            long total = (long)milliseconds * TimeSpan.TicksPerMillisecond;
            long start = DateTime.UtcNow.Ticks;
            double volume = Volume;

            while (!_stopRequested)
            {
                long elapsed = DateTime.UtcNow.Ticks - start;
                if (elapsed >= total)
                {
                    break;
                }

                int remaining = (int)((total - elapsed) / TimeSpan.TicksPerMillisecond);
                if (remaining < 1)
                {
                    break;
                }

                double ratio = (double)elapsed / total;
                ToneCore(frequencyAt(ratio), remaining < stepMs ? remaining : stepMs, fadeOut ? volume * (1.0 - ratio) : volume);
            }
        }

        /// <summary>The melody string of a ready-made effect (so that it can be mixed with notes in a melody).</summary>
        public static string EffectMelody(PicoCalcEffect effect)
        {
            switch (effect)
            {
                case PicoCalcEffect.Laser: return "S:2400>300:130";
                case PicoCalcEffect.Jump: return "S:300>1200:130";
                case PicoCalcEffect.Coin: return "B5:70 E6:250";
                case PicoCalcEffect.Siren:
                    return "S:500>1200:140 S:1200>500:140 S:500>1200:140 S:1200>500:140 S:500>1200:140 S:1200>500:140";
                case PicoCalcEffect.Explosion: return "NF:60-600:900";
                case PicoCalcEffect.Wind: return "NF:800-4000:1000";
                case PicoCalcEffect.Fall: return "SF:1500>100:600";
                case PicoCalcEffect.Tick: return "A6:8 R:90 A6:8 R:90 A6:8 R:90 A6:8 R:90 A6:8 R:90 A6:8 R:90";
                default: return "";
            }
        }

        /// <summary>Play a ready-made effect. Blocks until it is over or Stop() is called.</summary>
        public void PlayEffect(PicoCalcEffect effect)
        {
            Play(EffectMelody(effect));
        }

        /// <summary>Play a ready-made effect in the background and return immediately.</summary>
        public void PlayEffectAsync(PicoCalcEffect effect)
        {
            PlayAsync(EffectMelody(effect));
        }

        /// <summary>Play a melody (see the format at the top of the file). Blocks until it is over or Stop() is called.</summary>
        public void Play(string melody)
        {
            _stopRequested = false;
            string[] tokens = melody.Split(' ');

            for (int i = 0; i < tokens.Length && !_stopRequested; i++)
            {
                string token = tokens[i];
                if (token.Length == 0)
                {
                    continue;
                }

                string[] parts = token.Split(':');
                int milliseconds;

                if (parts.Length == 2)
                {
                    // A note or a rest: NOTE:milliseconds
                    if (parts[0].Length == 0 || !TryParseInt(parts[1], out milliseconds))
                    {
                        continue;   // malformed token: ignored
                    }

                    int frequency = ParseNote(parts[0]);
                    if (frequency < 0)
                    {
                        continue;   // unknown note: ignored
                    }

                    Tone(frequency, milliseconds);
                }
                else if (parts.Length == 3)
                {
                    // An effect: S:from>to:milliseconds, SF:..., N:min-max:milliseconds, NF:...
                    if (!TryParseInt(parts[2], out milliseconds))
                    {
                        continue;
                    }

                    string kind = parts[0].ToUpper();
                    int first;
                    int second;

                    if ((kind == "S" || kind == "SF") && TryParseRange(parts[1], '>', out first, out second))
                    {
                        SweepCore(first, second, milliseconds, kind == "SF");
                    }
                    else if ((kind == "N" || kind == "NF") && TryParseRange(parts[1], '-', out first, out second))
                    {
                        NoiseCore(first, second, milliseconds, kind == "NF");
                    }
                    // anything else: ignored
                }
            }
        }

        private static bool TryParseInt(string text, out int value)
        {
            try
            {
                value = int.Parse(text);
                return true;
            }
            catch (Exception)
            {
                value = 0;
                return false;
            }
        }

        // "2400>300" or "100-3000": two integers around the separator
        private static bool TryParseRange(string text, char separator, out int first, out int second)
        {
            first = 0;
            second = 0;

            int position = text.IndexOf(separator);
            if (position < 1 || position == text.Length - 1)
            {
                return false;
            }

            return TryParseInt(text.Substring(0, position), out first)
                && TryParseInt(text.Substring(position + 1), out second);
        }

        /// <summary>Start playing a melody in the background and return immediately.</summary>
        public void PlayAsync(string melody)
        {
            Stop();

            _thread = new Thread(() => Play(melody));
            _thread.Start();
        }

        /// <summary>Stop a melody (at the end of the current note).</summary>
        public void Stop()
        {
            _stopRequested = true;

            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join();
            }

            _thread = null;
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>Frequency in Hz of a note name such as "A4", "C#5", "Bb3"; 0 for a rest ("R"); -1 if unknown.</summary>
        public static int ParseNote(string name)
        {
            if (name.Length == 0)
            {
                return -1;
            }

            char letter = name[0];
            if (letter == 'R' || letter == 'r')
            {
                return 0;
            }

            int semitone;
            switch (letter)
            {
                case 'C': case 'c': semitone = 0; break;
                case 'D': case 'd': semitone = 2; break;
                case 'E': case 'e': semitone = 4; break;
                case 'F': case 'f': semitone = 5; break;
                case 'G': case 'g': semitone = 7; break;
                case 'A': case 'a': semitone = 9; break;
                case 'B': case 'b': semitone = 11; break;
                default: return -1;
            }

            int pos = 1;
            if (pos < name.Length && name[pos] == '#')
            {
                semitone++;
                pos++;
            }
            else if (pos < name.Length && name[pos] == 'b')
            {
                semitone--;
                pos++;
            }

            if (pos >= name.Length || name[pos] < '0' || name[pos] > '9')
            {
                return -1;
            }

            int octave = name[pos] - '0';

            // A flat on C or a sharp on B moves to the neighbouring octave
            if (semitone < 0)
            {
                semitone += 12;
                octave--;
            }
            else if (semitone > 11)
            {
                semitone -= 12;
                octave++;
            }

            int frequency = Octave4[semitone];
            int shift = octave - 4;
            while (shift > 0)
            {
                frequency *= 2;
                shift--;
            }

            while (shift < 0)
            {
                frequency /= 2;
                shift++;
            }

            return frequency;
        }
    }
}
