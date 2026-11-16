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

// PicoCalcSound - sound for the PicoCalc (.NET nanoFramework): beeps, notes, melodies.
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
//
// Melody format: space-separated tokens "NOTE:milliseconds". NOTE = letter A..G, optional '#' (sharp)
// or 'b' (flat), octave digit (A4 = 440 Hz). "R:200" is a rest. Example: "F#3:300 Bb4:150 R:100".
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

    public class PicoCalcSound : IDisposable
    {
        public const int PinLeft = 26;
        public const int PinRight = 27;

        // Frequencies of octave 4, from C to B
        private static readonly int[] Octave4 = new int[] { 262, 277, 294, 311, 330, 349, 370, 392, 415, 440, 466, 494 };

        private bool _stopRequested;
        private Thread _thread;

        /// <summary>Loudness from 0.0 (silent) to 1.0 (maximum). Default 0.6.</summary>
        public double Volume = 0.6;

        /// <summary>Speaker(s) used by Beep, Tone and Play. Default: left only. Left, Right and Both all
        /// work (GP26 and GP27 are the two outputs of the same PWM slice). Needs a firmware with the PWM
        /// clock divider fix (CLR 1.18.0.96 or later), see docs/sound.md.</summary>
        public PicoCalcSpeaker Speaker = PicoCalcSpeaker.Left;

        /// <summary>Pause in milliseconds added after each note, so repeated notes stay distinct.</summary>
        public int NoteGap = 15;

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
            if (frequency <= 0 || Volume <= 0.0)
            {
                Thread.Sleep(milliseconds);
                return;
            }

            // Duty cycle 0.5 is the loudest for a square wave
            double duty = Volume > 1.0 ? 0.5 : Volume * 0.5;

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

            if (NoteGap > 0)
            {
                Thread.Sleep(NoteGap);
            }
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

                int colon = token.IndexOf(':');
                if (colon < 1 || colon == token.Length - 1)
                {
                    continue;   // malformed token: ignored
                }

                int milliseconds;
                try
                {
                    milliseconds = int.Parse(token.Substring(colon + 1));
                }
                catch (Exception)
                {
                    continue;
                }

                int frequency = ParseNote(token.Substring(0, colon));
                if (frequency < 0)
                {
                    continue;   // unknown note: ignored
                }

                Tone(frequency, milliseconds);
            }
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
