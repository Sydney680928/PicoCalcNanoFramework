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

// PicoCalc FAT16 / FAT32 file system on top of PicoCalcSdCard (read, write, delete, create folders, rename)
//
// What it does:
//   - finds the first FAT partition in the MBR (or a card formatted without partition table)
//   - lists directories, opens files and reads them completely
//   - short names only (8.3, e.g. HELLO.TXT). Long file names are skipped. Matching is case-insensitive.
//   - paths use '/' (or '\'): "HELLO.TXT", "TEST/NOTE.TXT", "/" for the root
//
// Writing: WriteFile / WriteAllText create or replace a file (8.3 name, upper case), DeleteFile,
// CreateDirectory, DeleteDirectory (empty folders only), Rename (same folder). Not supported yet:
// growing a full directory, FAT12 (cards under 8 MB), exFAT, creating long file names. No clock on the Pico: the date stored in files is FileDate / FileTime
// (set them with SetTimestamp). The FAT32 "free clusters" hint is invalidated on the first write.
//
// NuGet packages needed: the ones of PicoCalcSdCard (Spi, Gpio). PicoCalcSdCard.cs must be in the project.
//
// Usage:
//     var sd = new PicoCalcSdCard();
//     sd.Init();
//     var fat = new PicoCalcFat(sd);
//     if (fat.Mount())
//     {
//         PicoCalcFatEntry[] files = fat.ListDirectory("/");
//         string text = fat.ReadAllText("HELLO.TXT");
//     }

using System;
using System.Collections;

namespace PicoCalcNano
{
    /// <summary>One file or directory found in a directory.</summary>
    public class PicoCalcFatEntry
    {
        public string Name = "";
        public bool IsDirectory;
        public uint Size;
        public uint FirstCluster;
    }

    public class PicoCalcFat
    {
        private readonly PicoCalcSdCard _card;

        private readonly byte[] _sector = new byte[512];   // data / directory sector
        private readonly byte[] _fatBuf = new byte[512];   // one cached sector of the FAT
        private uint _fatCacheSector = 0xFFFFFFFF;

        private bool _mounted;
        private bool _fat32;
        private uint _sectorsPerCluster;
        private uint _fatStart;        // absolute sector of the first FAT
        private uint _rootDirStart;    // absolute sector of the FAT16 root directory
        private uint _rootDirSectors;  // size of the FAT16 root directory, in sectors
        private uint _dataStart;       // absolute sector of cluster 2
        private uint _rootCluster;     // FAT32 root directory cluster
        private uint _endMark;         // cluster values >= this one end a chain (or are bad)
        private uint _totalClusters;

        // Write support
        private readonly byte[] _wbuf = new byte[512];
        private bool _fatDirty;            // _fatBuf holds changes not yet written to the card
        private bool _fsInfoDone;
        private uint _partitionStart;
        private uint _numFats;
        private uint _fatSize;             // sectors per FAT
        private uint _fsInfoSector;
        private uint _freeHint = 2;        // where to start looking for free clusters
        private bool _slotFound;
        private uint _slotSector;
        private int _slotOffset;
        private bool _freeFound;
        private uint _freeSector;
        private int _freeOffset;
        private const int MaxLfnEntries = 20;
        private readonly uint[] _lfnSector = new uint[MaxLfnEntries];   // long-name entries just before the entry found
        private readonly int[] _lfnOffset = new int[MaxLfnEntries];
        private int _lfnCount;
        private bool _itRoot16;
        private uint _itCluster;
        private uint _itIndex;
        private string _error = "";

        /// <summary>Largest file ReadFile will load in memory, in bytes (the Pico has little RAM).</summary>
        public int MaxReadSize = 65536;

        /// <summary>Date stored in files written by WriteFile (FAT format). Default: 2026-11-11.</summary>
        public ushort FileDate = 0x5D6B;

        /// <summary>Time stored in files written by WriteFile (FAT format).</summary>
        public ushort FileTime = 0;

        /// <summary>Sets the date and time stored in the files written afterwards.</summary>
        public void SetTimestamp(int year, int month, int day, int hour, int minute, int second)
        {
            FileDate = (ushort)(((year - 1980) << 9) | (month << 5) | day);
            FileTime = (ushort)((hour << 11) | (minute << 5) | (second / 2));
        }

        public string ErrorMessage { get { return _error; } }
        public string FatType { get { return _fat32 ? "FAT32" : "FAT16"; } }
        public bool IsMounted { get { return _mounted; } }
        public uint ClusterSizeBytes { get { return _sectorsPerCluster * 512; } }
        public uint TotalClusters { get { return _totalClusters; } }

        public PicoCalcFat(PicoCalcSdCard card)
        {
            _card = card;
        }

        /// <summary>Reads the partition table and the boot sector. Returns false on failure (see ErrorMessage).</summary>
        public bool Mount()
        {
            _mounted = false;
            _error = "";
            _fatCacheSector = 0xFFFFFFFF;

            if (!ReadSector(0, _sector))
            {
                return false;
            }

            if (_sector[510] != 0x55 || _sector[511] != 0xAA)
            {
                _error = "Bloc 0 sans signature 55 AA";
                return false;
            }

            uint partitionStart = 0;
            if (!LooksLikeBootSector(_sector))
            {
                // MBR: first FAT16 / FAT32 partition
                bool found = false;
                for (int i = 0; i < 4; i++)
                {
                    int offset = 446 + 16 * i;
                    byte type = _sector[offset + 4];
                    if (type == 0x04 || type == 0x06 || type == 0x0E || type == 0x0B || type == 0x0C)
                    {
                        partitionStart = U32(_sector, offset + 8);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    _error = "Aucune partition FAT16 / FAT32 trouvee";
                    return false;
                }

                if (!ReadSector(partitionStart, _sector))
                {
                    return false;
                }

                if (_sector[510] != 0x55 || _sector[511] != 0xAA || !LooksLikeBootSector(_sector))
                {
                    _error = "Secteur de demarrage de la partition invalide";
                    return false;
                }
            }

            // BIOS Parameter Block
            uint bytesPerSector = U16(_sector, 11);
            _sectorsPerCluster = _sector[13];
            uint reserved = U16(_sector, 14);
            uint fats = _sector[16];
            uint rootEntries = U16(_sector, 17);
            uint totalSectors = U16(_sector, 19);
            uint fatSize = U16(_sector, 22);
            if (totalSectors == 0)
            {
                totalSectors = U32(_sector, 32);
            }

            if (fatSize == 0)
            {
                fatSize = U32(_sector, 36);
                _rootCluster = U32(_sector, 44);
            }

            if (bytesPerSector != 512 || _sectorsPerCluster == 0 || fats == 0 || fatSize == 0)
            {
                _error = "Parametres FAT invalides (octets par secteur = " + bytesPerSector + ")";
                return false;
            }

            _rootDirSectors = ((rootEntries * 32) + 511) / 512;
            uint dataOffset = reserved + fats * fatSize + _rootDirSectors;
            _totalClusters = (totalSectors - dataOffset) / _sectorsPerCluster;

            if (_totalClusters < 4085)
            {
                _error = "FAT12 non supporte";
                return false;
            }

            _fat32 = _totalClusters >= 65525;
            _numFats = fats;
            _fatSize = fatSize;
            _partitionStart = partitionStart;
            _fsInfoSector = _fat32 ? U16(_sector, 48) : 0;
            _fatDirty = false;
            _fsInfoDone = false;
            _freeHint = 2;
            _endMark = _fat32 ? 0x0FFFFFF7u : 0xFFF7u;

            _fatStart = partitionStart + reserved;
            _rootDirStart = _fatStart + fats * fatSize;
            _dataStart = partitionStart + dataOffset;

            _mounted = true;
            return true;
        }

        /// <summary>Lists a directory ("/" for the root). Returns null on error.</summary>
        public PicoCalcFatEntry[] ListDirectory(string path)
        {
            if (!CheckMounted())
            {
                return null;
            }

            PicoCalcFatEntry dir = Find(path);
            if (dir == null)
            {
                return null;
            }

            if (!dir.IsDirectory)
            {
                _error = "Ce n'est pas un dossier : " + path;
                return null;
            }

            ArrayList list = ReadDirEntries(dir.FirstCluster);
            if (list == null)
            {
                return null;
            }

            return (PicoCalcFatEntry[])list.ToArray(typeof(PicoCalcFatEntry));
        }

        /// <summary>Returns information on a file or directory, or null if it does not exist.</summary>
        public PicoCalcFatEntry GetEntry(string path)
        {
            if (!CheckMounted())
            {
                return null;
            }

            return Find(path);
        }

        /// <summary>Reads a whole file (up to MaxReadSize bytes). Returns null on error.</summary>
        public byte[] ReadFile(string path)
        {
            if (!CheckMounted())
            {
                return null;
            }

            PicoCalcFatEntry entry = Find(path);
            if (entry == null)
            {
                return null;
            }

            if (entry.IsDirectory)
            {
                _error = "C'est un dossier : " + path;
                return null;
            }

            if (entry.Size > (uint)MaxReadSize)
            {
                _error = "Fichier trop gros (" + entry.Size + " octets, maximum " + MaxReadSize + ")";
                return null;
            }

            byte[] result = new byte[(int)entry.Size];
            int position = 0;
            uint remaining = entry.Size;
            uint cluster = entry.FirstCluster;

            while (remaining > 0 && cluster >= 2 && cluster < _endMark)
            {
                uint sector = ClusterToSector(cluster);
                for (uint i = 0; i < _sectorsPerCluster && remaining > 0; i++)
                {
                    if (!ReadSector(sector + i, _sector))
                    {
                        return null;
                    }

                    int count = remaining > 512 ? 512 : (int)remaining;
                    Array.Copy(_sector, 0, result, position, count);
                    position += count;
                    remaining -= (uint)count;
                }

                cluster = NextCluster(cluster);
            }

            if (remaining > 0)
            {
                _error = "Chaine de clusters interrompue avant la fin du fichier";
                return null;
            }

            return result;
        }

        /// <summary>Reads a whole text file as UTF-8. Returns null on error.</summary>
        public string ReadAllText(string path)
        {
            byte[] data = ReadFile(path);
            if (data == null)
            {
                return null;
            }

            return DecodeUtf8(data);
        }

        // UTF-8 decoder (no System.Text package needed). A byte that is not valid UTF-8 is kept as
        // a Latin-1 character; characters outside the 16-bit range come out as several characters.
        private static string DecodeUtf8(byte[] d)
        {
            char[] chars = new char[d.Length];
            int n = 0;
            int i = 0;

            if (d.Length >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF)
            {
                i = 3;   // skip the byte order mark
            }

            while (i < d.Length)
            {
                byte b = d[i];
                if (b < 0x80)
                {
                    chars[n++] = (char)b;
                    i++;
                }
                else if ((b & 0xE0) == 0xC0 && i + 1 < d.Length && (d[i + 1] & 0xC0) == 0x80)
                {
                    chars[n++] = (char)(((b & 0x1F) << 6) | (d[i + 1] & 0x3F));
                    i += 2;
                }
                else if ((b & 0xF0) == 0xE0 && i + 2 < d.Length && (d[i + 1] & 0xC0) == 0x80 && (d[i + 2] & 0xC0) == 0x80)
                {
                    chars[n++] = (char)(((b & 0x0F) << 12) | ((d[i + 1] & 0x3F) << 6) | (d[i + 2] & 0x3F));
                    i += 3;
                }
                else
                {
                    chars[n++] = (char)b;
                    i++;
                }
            }

            return new string(chars, 0, n);
        }

        // ---------------------------------------------------------------- directories

        // Finds the entry for a path. The root is returned as a directory with FirstCluster = 0.
        private PicoCalcFatEntry Find(string path)
        {
            PicoCalcFatEntry current = new PicoCalcFatEntry();
            current.IsDirectory = true;
            current.FirstCluster = 0;

            string[] parts = path.Split(new char[] { '/', '\\' });
            for (int p = 0; p < parts.Length; p++)
            {
                string part = parts[p];
                if (part.Length == 0)
                {
                    continue;
                }

                if (!current.IsDirectory)
                {
                    _error = "Ce n'est pas un dossier : " + current.Name;
                    return null;
                }

                ArrayList entries = ReadDirEntries(current.FirstCluster);
                if (entries == null)
                {
                    return null;
                }

                string wanted = part.ToUpper();
                PicoCalcFatEntry found = null;
                for (int i = 0; i < entries.Count; i++)
                {
                    PicoCalcFatEntry e = (PicoCalcFatEntry)entries[i];
                    if (e.Name.ToUpper() == wanted)
                    {
                        found = e;
                        break;
                    }
                }

                if (found == null)
                {
                    _error = "Introuvable : " + part;
                    return null;
                }

                current = found;
            }

            return current;
        }

        // Reads all entries of a directory. cluster = 0 means the root directory.
        private ArrayList ReadDirEntries(uint cluster)
        {
            ArrayList list = new ArrayList();

            if (cluster == 0 && !_fat32)
            {
                // FAT16: the root directory is a fixed area before the data
                for (uint i = 0; i < _rootDirSectors; i++)
                {
                    if (!ReadSector(_rootDirStart + i, _sector))
                    {
                        return null;
                    }

                    if (!ParseDirSector(list))
                    {
                        break;
                    }
                }

                return list;
            }

            if (cluster == 0)
            {
                cluster = _rootCluster;
            }

            int guard = 0;
            bool more = true;
            while (more && cluster >= 2 && cluster < _endMark && guard < 100000)
            {
                uint sector = ClusterToSector(cluster);
                for (uint i = 0; i < _sectorsPerCluster; i++)
                {
                    if (!ReadSector(sector + i, _sector))
                    {
                        return null;
                    }

                    if (!ParseDirSector(list))
                    {
                        more = false;
                        break;
                    }
                }

                if (more)
                {
                    cluster = NextCluster(cluster);
                }

                guard++;
            }

            return list;
        }

        // Parses the 16 directory entries of _sector. Returns false when the end marker is reached.
        private bool ParseDirSector(ArrayList list)
        {
            for (int offset = 0; offset < 512; offset += 32)
            {
                byte first = _sector[offset];
                if (first == 0x00)
                {
                    return false;           // no more entries in this directory
                }

                if (first == 0xE5)
                {
                    continue;               // deleted
                }

                byte attributes = _sector[offset + 11];
                if (attributes == 0x0F || (attributes & 0x08) != 0)
                {
                    continue;               // long file name part or volume label
                }

                string name = ShortName(offset);
                if (name == "." || name == "..")
                {
                    continue;
                }

                PicoCalcFatEntry entry = new PicoCalcFatEntry();
                entry.Name = name;
                entry.IsDirectory = (attributes & 0x10) != 0;
                entry.Size = U32(_sector, offset + 28);

                uint cluster = U16(_sector, offset + 26);
                if (_fat32)
                {
                    cluster |= U16(_sector, offset + 20) << 16;
                }

                entry.FirstCluster = cluster;
                list.Add(entry);
            }

            return true;
        }

        // Builds "NAME.EXT" from the 8 + 3 characters at _sector[offset]
        private string ShortName(int offset)
        {
            char[] chars = new char[12];
            int n = 0;

            int baseLength = 8;
            while (baseLength > 0 && _sector[offset + baseLength - 1] == 0x20)
            {
                baseLength--;
            }

            for (int i = 0; i < baseLength; i++)
            {
                byte b = _sector[offset + i];
                if (i == 0 && b == 0x05)
                {
                    b = 0xE5;
                }

                chars[n++] = (char)b;
            }

            int extLength = 3;
            while (extLength > 0 && _sector[offset + 8 + extLength - 1] == 0x20)
            {
                extLength--;
            }

            if (extLength > 0)
            {
                chars[n++] = '.';
                for (int i = 0; i < extLength; i++)
                {
                    chars[n++] = (char)_sector[offset + 8 + i];
                }
            }

            return new string(chars, 0, n);
        }


        // ---------------------------------------------------------------- writing

        /// <summary>Creates a file, or replaces it if it exists (8.3 name, e.g. "NOTE.TXT" or "TEST/NOTE.TXT";
        /// the parent folder must exist). Returns false on error (see ErrorMessage).</summary>
        public bool WriteFile(string path, byte[] data)
        {
            if (!CheckMounted())
            {
                return false;
            }

            if (data == null)
            {
                _error = "Donnees nulles";
                return false;
            }

            // Split into parent folder and file name
            string[] parts = path.Split(new char[] { '/', '\\' });
            int last = -1;
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (parts[i].Length > 0)
                {
                    last = i;
                    break;
                }
            }

            if (last < 0)
            {
                _error = "Chemin vide";
                return false;
            }

            string fileName = parts[last];
            string parent = "";
            for (int i = 0; i < last; i++)
            {
                if (parts[i].Length > 0)
                {
                    parent += parts[i] + "/";
                }
            }

            byte[] name11 = ToShortName(fileName);
            if (name11 == null)
            {
                _error = "Nom non valide (8.3, lettres, chiffres, _ - ~) : " + fileName;
                return false;
            }

            PicoCalcFatEntry dir = Find(parent);
            if (dir == null)
            {
                return false;
            }

            if (!dir.IsDirectory)
            {
                _error = "Ce n'est pas un dossier : " + parent;
                return false;
            }

            if (!InvalidateFsInfo())
            {
                return false;
            }

            if (!ScanDirectory(dir.FirstCluster, name11))
            {
                return false;
            }

            bool exists = _slotFound;
            uint entrySector;
            int entryOffset;

            if (exists)
            {
                entrySector = _slotSector;
                entryOffset = _slotOffset;

                if (!ReadSector(entrySector, _sector))
                {
                    return false;
                }

                byte attributes = _sector[entryOffset + 11];
                if ((attributes & 0x10) != 0)
                {
                    _error = "C'est un dossier : " + fileName;
                    return false;
                }

                if ((attributes & 0x01) != 0)
                {
                    _error = "Fichier en lecture seule : " + fileName;
                    return false;
                }

                uint oldFirst = U16(_sector, entryOffset + 26);
                if (_fat32)
                {
                    oldFirst |= U16(_sector, entryOffset + 20) << 16;
                }

                if (!FreeChain(oldFirst))
                {
                    return false;
                }
            }
            else
            {
                if (!_freeFound)
                {
                    _error = "Dossier plein (l'agrandissement d'un dossier n'est pas gere)";
                    return false;
                }

                entrySector = _freeSector;
                entryOffset = _freeOffset;
            }

            // Allocate and link the clusters
            uint clusterBytes = _sectorsPerCluster * 512;
            uint needed = ((uint)data.Length + clusterBytes - 1) / clusterBytes;
            uint[] clusters = null;
            uint firstCluster = 0;

            if (needed > 0)
            {
                clusters = AllocateClusters(needed);
                if (clusters == null)
                {
                    return false;
                }

                uint endOfChain = _fat32 ? 0x0FFFFFFFu : 0xFFFFu;
                for (uint i = 0; i < needed; i++)
                {
                    if (!SetFat(clusters[i], i + 1 < needed ? clusters[i + 1] : endOfChain))
                    {
                        return false;
                    }
                }

                firstCluster = clusters[0];
            }

            // Write the data
            int position = 0;
            for (uint c = 0; c < needed; c++)
            {
                uint sector = ClusterToSector(clusters[c]);
                for (uint i = 0; i < _sectorsPerCluster && position < data.Length; i++)
                {
                    for (int k = 0; k < 512; k++)
                    {
                        _wbuf[k] = 0;
                    }

                    int count = data.Length - position;
                    if (count > 512)
                    {
                        count = 512;
                    }

                    Array.Copy(data, position, _wbuf, 0, count);
                    if (!WriteSector(sector + i, _wbuf))
                    {
                        return false;
                    }

                    position += 512;
                }
            }

            // FAT first, directory entry last
            if (!FlushFat())
            {
                return false;
            }

            if (!ReadSector(entrySector, _sector))
            {
                return false;
            }

            if (!exists)
            {
                for (int i = 0; i < 32; i++)
                {
                    _sector[entryOffset + i] = 0;
                }

                for (int i = 0; i < 11; i++)
                {
                    _sector[entryOffset + i] = name11[i];
                }

                _sector[entryOffset + 11] = 0x20;   // archive
                PutU16(_sector, entryOffset + 14, FileTime);
                PutU16(_sector, entryOffset + 16, FileDate);
            }

            PutU16(_sector, entryOffset + 18, FileDate);
            PutU16(_sector, entryOffset + 20, _fat32 ? (firstCluster >> 16) : 0);
            PutU16(_sector, entryOffset + 22, FileTime);
            PutU16(_sector, entryOffset + 24, FileDate);
            PutU16(_sector, entryOffset + 26, firstCluster & 0xFFFF);
            PutU32(_sector, entryOffset + 28, (uint)data.Length);

            return WriteSector(entrySector, _sector);
        }

        /// <summary>Creates or replaces a text file, encoded as UTF-8.</summary>
        public bool WriteAllText(string path, string text)
        {
            return WriteFile(path, EncodeUtf8(text));
        }

        /// <summary>Deletes a file.</summary>
        public bool DeleteFile(string path)
        {
            if (!CheckMounted())
            {
                return false;
            }

            PicoCalcFatEntry parent;
            byte[] name11;
            if (!Locate(path, out parent, out name11))
            {
                return false;
            }

            if (!_slotFound)
            {
                _error = "Introuvable : " + path;
                return false;
            }

            return RemoveEntry(name11, false);
        }

        /// <summary>Deletes an EMPTY folder.</summary>
        public bool DeleteDirectory(string path)
        {
            if (!CheckMounted())
            {
                return false;
            }

            PicoCalcFatEntry parent;
            byte[] name11;
            if (!Locate(path, out parent, out name11))
            {
                return false;
            }

            if (!_slotFound)
            {
                _error = "Introuvable : " + path;
                return false;
            }

            return RemoveEntry(name11, true);
        }

        /// <summary>Creates a folder (8.3 name). The parent folder must exist.</summary>
        public bool CreateDirectory(string path)
        {
            if (!CheckMounted())
            {
                return false;
            }

            PicoCalcFatEntry parent;
            byte[] name11;
            if (!Locate(path, out parent, out name11))
            {
                return false;
            }

            if (_slotFound)
            {
                _error = "Existe deja : " + path;
                return false;
            }

            if (!_freeFound)
            {
                _error = "Dossier plein (l'agrandissement d'un dossier n'est pas gere)";
                return false;
            }

            uint entrySector = _freeSector;
            int entryOffset = _freeOffset;
            uint parentCluster = parent.FirstCluster;   // 0 for the root, as required by ".."

            if (!InvalidateFsInfo())
            {
                return false;
            }

            uint[] allocated = AllocateClusters(1);
            if (allocated == null)
            {
                return false;
            }

            uint cluster = allocated[0];
            if (!SetFat(cluster, _fat32 ? 0x0FFFFFFFu : 0xFFFFu))
            {
                return false;
            }

            if (!FlushFat())
            {
                return false;
            }

            // The whole cluster is written: "." and ".." first, then zeros (no stale entries left behind)
            uint firstSector = ClusterToSector(cluster);
            for (uint i = 0; i < _sectorsPerCluster; i++)
            {
                for (int k = 0; k < 512; k++)
                {
                    _wbuf[k] = 0;
                }

                if (i == 0)
                {
                    FillDotEntry(_wbuf, 0, false, cluster);
                    FillDotEntry(_wbuf, 32, true, parentCluster);
                }

                if (!WriteSector(firstSector + i, _wbuf))
                {
                    return false;
                }
            }

            // Directory entry last
            if (!ReadSector(entrySector, _sector))
            {
                return false;
            }

            for (int i = 0; i < 32; i++)
            {
                _sector[entryOffset + i] = 0;
            }

            for (int i = 0; i < 11; i++)
            {
                _sector[entryOffset + i] = name11[i];
            }

            _sector[entryOffset + 11] = 0x10;   // directory
            PutU16(_sector, entryOffset + 14, FileTime);
            PutU16(_sector, entryOffset + 16, FileDate);
            PutU16(_sector, entryOffset + 18, FileDate);
            PutU16(_sector, entryOffset + 20, _fat32 ? (cluster >> 16) : 0);
            PutU16(_sector, entryOffset + 22, FileTime);
            PutU16(_sector, entryOffset + 24, FileDate);
            PutU16(_sector, entryOffset + 26, cluster & 0xFFFF);

            return WriteSector(entrySector, _sector);
        }

        /// <summary>Renames a file or folder inside its folder. newName is a bare 8.3 name (no path).</summary>
        public bool Rename(string path, string newName)
        {
            if (!CheckMounted())
            {
                return false;
            }

            PicoCalcFatEntry parent;
            byte[] oldName11;
            if (!Locate(path, out parent, out oldName11))
            {
                return false;
            }

            if (!_slotFound)
            {
                _error = "Introuvable : " + path;
                return false;
            }

            byte[] newName11 = ToShortName(newName);
            if (newName11 == null)
            {
                _error = "Nouveau nom non valide (8.3, lettres, chiffres, _ - ~) : " + newName;
                return false;
            }

            bool same = true;
            for (int i = 0; i < 11; i++)
            {
                if (oldName11[i] != newName11[i])
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return true;
            }

            // The new name must not exist yet
            if (!ScanDirectory(parent.FirstCluster, newName11))
            {
                return false;
            }

            if (_slotFound)
            {
                _error = "Existe deja : " + newName;
                return false;
            }

            // Back to the entry to rename
            if (!ScanDirectory(parent.FirstCluster, oldName11) || !_slotFound)
            {
                return false;
            }

            if (!InvalidateFsInfo())
            {
                return false;
            }

            uint sector = _slotSector;
            int offset = _slotOffset;

            // A long name (if any) would no longer match: remove it
            if (!DeleteLfnEntries(oldName11))
            {
                return false;
            }

            if (!ReadSector(sector, _sector))
            {
                return false;
            }

            for (int i = 0; i < 11; i++)
            {
                _sector[offset + i] = newName11[i];
            }

            return WriteSector(sector, _sector);
        }

        // Removes the entry found by the last ScanDirectory (a file, or an empty folder)
        private bool RemoveEntry(byte[] name11, bool directory)
        {
            uint sector = _slotSector;
            int offset = _slotOffset;

            if (!ReadSector(sector, _sector))
            {
                return false;
            }

            byte attributes = _sector[offset + 11];
            bool isDirectory = (attributes & 0x10) != 0;
            if (isDirectory != directory)
            {
                _error = directory ? "Ce n'est pas un dossier" : "C'est un dossier (utiliser DeleteDirectory)";
                return false;
            }

            if ((attributes & 0x01) != 0)
            {
                _error = "Lecture seule";
                return false;
            }

            uint first = U16(_sector, offset + 26);
            if (_fat32)
            {
                first |= U16(_sector, offset + 20) << 16;
            }

            if (directory)
            {
                ArrayList content = ReadDirEntries(first);
                if (content == null)
                {
                    return false;
                }

                if (content.Count > 0)
                {
                    _error = "Le dossier n'est pas vide";
                    return false;
                }
            }

            if (!InvalidateFsInfo())
            {
                return false;
            }

            if (!FreeChain(first))
            {
                return false;
            }

            if (!FlushFat())
            {
                return false;
            }

            if (!DeleteLfnEntries(name11))
            {
                return false;
            }

            if (!ReadSector(sector, _sector))
            {
                return false;
            }

            _sector[offset] = 0xE5;
            return WriteSector(sector, _sector);
        }

        // Marks as deleted the long-name entries recorded by the last ScanDirectory, if they belong to name11
        private bool DeleteLfnEntries(byte[] name11)
        {
            byte checksum = ShortNameChecksum(name11);
            for (int i = 0; i < _lfnCount; i++)
            {
                if (!ReadSector(_lfnSector[i], _wbuf))
                {
                    return false;
                }

                if (_wbuf[_lfnOffset[i] + 13] == checksum)
                {
                    _wbuf[_lfnOffset[i]] = 0xE5;
                    if (!WriteSector(_lfnSector[i], _wbuf))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static byte ShortNameChecksum(byte[] name11)
        {
            byte sum = 0;
            for (int i = 0; i < 11; i++)
            {
                sum = (byte)((((sum & 1) != 0) ? 0x80 : 0) + (sum >> 1) + name11[i]);
            }

            return sum;
        }

        // The "." and ".." entries of a new folder
        private void FillDotEntry(byte[] buffer, int offset, bool twoDots, uint cluster)
        {
            for (int i = 0; i < 11; i++)
            {
                buffer[offset + i] = 0x20;
            }

            buffer[offset] = 0x2E;
            if (twoDots)
            {
                buffer[offset + 1] = 0x2E;
            }

            buffer[offset + 11] = 0x10;
            PutU16(buffer, offset + 14, FileTime);
            PutU16(buffer, offset + 16, FileDate);
            PutU16(buffer, offset + 18, FileDate);
            PutU16(buffer, offset + 20, _fat32 ? (cluster >> 16) : 0);
            PutU16(buffer, offset + 22, FileTime);
            PutU16(buffer, offset + 24, FileDate);
            PutU16(buffer, offset + 26, cluster & 0xFFFF);
        }

        // Splits "A/B/C.TXT" into the parent folder path ("A/B/") and the last name ("C.TXT")
        private bool SplitPath(string path, out string parentPath, out string name)
        {
            parentPath = "";
            name = "";

            string[] parts = path.Split(new char[] { '/', '\\' });
            int last = -1;
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (parts[i].Length > 0)
                {
                    last = i;
                    break;
                }
            }

            if (last < 0)
            {
                _error = "Chemin vide";
                return false;
            }

            name = parts[last];
            for (int i = 0; i < last; i++)
            {
                if (parts[i].Length > 0)
                {
                    parentPath += parts[i] + "/";
                }
            }

            return true;
        }

        // Finds the parent folder of a path and scans it for the last name (results in _slot* / _free* / _lfn*)
        private bool Locate(string path, out PicoCalcFatEntry parent, out byte[] name11)
        {
            parent = null;
            name11 = null;

            string parentPath;
            string fileName;
            if (!SplitPath(path, out parentPath, out fileName))
            {
                return false;
            }

            name11 = ToShortName(fileName);
            if (name11 == null)
            {
                _error = "Nom non valide (8.3, lettres, chiffres, _ - ~) : " + fileName;
                return false;
            }

            parent = Find(parentPath);
            if (parent == null)
            {
                return false;
            }

            if (!parent.IsDirectory)
            {
                _error = "Ce n'est pas un dossier : " + parentPath;
                return false;
            }

            return ScanDirectory(parent.FirstCluster, name11);
        }

        // Frees a whole cluster chain in the FAT
        private bool FreeChain(uint cluster)
        {
            int guard = 0;
            while (cluster >= 2 && cluster < _endMark && guard < 1000000)
            {
                uint next;
                if (!TryGetFat(cluster, out next))
                {
                    return false;
                }

                if (!SetFat(cluster, 0))
                {
                    return false;
                }

                cluster = next;
                guard++;
            }

            return true;
        }

        // Finds 'count' free clusters (not yet marked as used). Returns null if the card is full.
        private uint[] AllocateClusters(uint count)
        {
            uint[] result = new uint[count];
            uint found = 0;
            uint lastCluster = _totalClusters + 1;
            uint cluster = _freeHint;
            if (cluster < 2 || cluster > lastCluster)
            {
                cluster = 2;
            }

            uint scanned = 0;
            while (found < count && scanned < _totalClusters)
            {
                uint value;
                if (!TryGetFat(cluster, out value))
                {
                    return null;
                }

                if (value == 0)
                {
                    result[found] = cluster;
                    found++;
                }

                cluster++;
                if (cluster > lastCluster)
                {
                    cluster = 2;
                }

                scanned++;
            }

            if (found < count)
            {
                _error = "Carte pleine";
                return null;
            }

            _freeHint = cluster;
            return result;
        }

        private bool TryGetFat(uint cluster, out uint value)
        {
            value = 0;
            uint offset = _fat32 ? cluster * 4 : cluster * 2;
            uint sector = _fatStart + offset / 512;
            int index = (int)(offset % 512);

            if (!LoadFatSector(sector))
            {
                return false;
            }

            value = _fat32 ? (U32(_fatBuf, index) & 0x0FFFFFFF) : U16(_fatBuf, index);
            return true;
        }

        private bool SetFat(uint cluster, uint value)
        {
            uint offset = _fat32 ? cluster * 4 : cluster * 2;
            uint sector = _fatStart + offset / 512;
            int index = (int)(offset % 512);

            if (!LoadFatSector(sector))
            {
                return false;
            }

            if (_fat32)
            {
                uint old = U32(_fatBuf, index);
                PutU32(_fatBuf, index, (old & 0xF0000000u) | (value & 0x0FFFFFFFu));
            }
            else
            {
                PutU16(_fatBuf, index, value);
            }

            _fatDirty = true;
            return true;
        }

        // FAT32 only: the "free clusters" counters in the FSInfo sector become wrong when we allocate
        // clusters, so mark them unknown (0xFFFFFFFF) once, before the first write.
        private bool InvalidateFsInfo()
        {
            if (_fsInfoDone)
            {
                return true;
            }

            if (_fat32 && _fsInfoSector != 0 && _fsInfoSector != 0xFFFF)
            {
                uint sector = _partitionStart + _fsInfoSector;
                if (!ReadSector(sector, _wbuf))
                {
                    return false;
                }

                if (U32(_wbuf, 0) == 0x41615252 && U32(_wbuf, 484) == 0x61417272)
                {
                    for (int i = 488; i < 496; i++)
                    {
                        _wbuf[i] = 0xFF;
                    }

                    if (!WriteSector(sector, _wbuf))
                    {
                        return false;
                    }
                }
            }

            _fsInfoDone = true;
            return true;
        }

        // Looks for name11 in a directory: sets _slotFound/_slotSector/_slotOffset if it exists, and
        // _freeFound/_freeSector/_freeOffset for the first free entry.
        private bool ScanDirectory(uint dirCluster, byte[] name11)
        {
            _slotFound = false;
            _freeFound = false;
            _lfnCount = 0;
            IterStart(dirCluster);

            bool ended = false;
            while (!ended)
            {
                uint sector = IterNext();
                if (sector == 0xFFFFFFFF)
                {
                    break;
                }

                if (!ReadSector(sector, _sector))
                {
                    return false;
                }

                for (int offset = 0; offset < 512; offset += 32)
                {
                    byte first = _sector[offset];
                    if (first == 0x00)
                    {
                        if (!_freeFound)
                        {
                            _freeFound = true;
                            _freeSector = sector;
                            _freeOffset = offset;
                        }

                        ended = true;
                        break;
                    }

                    if (first == 0xE5)
                    {
                        if (!_freeFound)
                        {
                            _freeFound = true;
                            _freeSector = sector;
                            _freeOffset = offset;
                        }

                        _lfnCount = 0;
                        continue;
                    }

                    byte attributes = _sector[offset + 11];
                    if (attributes == 0x0F)
                    {
                        // long file name entry: remember it, it belongs to the short entry that follows
                        if (_lfnCount < MaxLfnEntries)
                        {
                            _lfnSector[_lfnCount] = sector;
                            _lfnOffset[_lfnCount] = offset;
                            _lfnCount++;
                        }

                        continue;
                    }

                    if ((attributes & 0x08) != 0)
                    {
                        _lfnCount = 0;
                        continue;
                    }

                    bool same = true;
                    for (int i = 0; i < 11; i++)
                    {
                        if (_sector[offset + i] != name11[i])
                        {
                            same = false;
                            break;
                        }
                    }

                    if (same)
                    {
                        _slotFound = true;
                        _slotSector = sector;
                        _slotOffset = offset;
                        ended = true;
                        break;
                    }
                    else
                    {
                        _lfnCount = 0;
                    }
                }
            }

            return true;
        }

        // Sector-by-sector walk through a directory (root area of FAT16, or cluster chain)
        private void IterStart(uint cluster)
        {
            _itIndex = 0;
            if (cluster == 0 && !_fat32)
            {
                _itRoot16 = true;
            }
            else
            {
                _itRoot16 = false;
                _itCluster = cluster == 0 ? _rootCluster : cluster;
            }
        }

        // Next directory sector, or 0xFFFFFFFF at the end
        private uint IterNext()
        {
            if (_itRoot16)
            {
                if (_itIndex >= _rootDirSectors)
                {
                    return 0xFFFFFFFF;
                }

                uint rootSector = _rootDirStart + _itIndex;
                _itIndex++;
                return rootSector;
            }

            if (_itCluster < 2 || _itCluster >= _endMark)
            {
                return 0xFFFFFFFF;
            }

            uint sector = ClusterToSector(_itCluster) + _itIndex;
            _itIndex++;
            if (_itIndex >= _sectorsPerCluster)
            {
                _itIndex = 0;
                _itCluster = NextCluster(_itCluster);
            }

            return sector;
        }

        // "note.txt" -> 11 bytes "NOTE    TXT" (null if the name is not a valid 8.3 name)
        private static byte[] ToShortName(string name)
        {
            string upper = name.ToUpper();
            int dot = upper.LastIndexOf('.');
            string baseName = dot < 0 ? upper : upper.Substring(0, dot);
            string extension = dot < 0 ? "" : upper.Substring(dot + 1);

            if (baseName.Length == 0 || baseName.Length > 8 || extension.Length > 3)
            {
                return null;
            }

            byte[] result = new byte[11];
            for (int i = 0; i < 11; i++)
            {
                result[i] = 0x20;
            }

            for (int i = 0; i < baseName.Length; i++)
            {
                if (!ValidNameChar(baseName[i]))
                {
                    return null;
                }

                result[i] = (byte)baseName[i];
            }

            for (int i = 0; i < extension.Length; i++)
            {
                if (!ValidNameChar(extension[i]))
                {
                    return null;
                }

                result[8 + i] = (byte)extension[i];
            }

            return result;
        }

        private static bool ValidNameChar(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '~'
                || c == '$' || c == '!' || c == '#' || c == '%' || c == '&' || c == '(' || c == ')' || c == '@' || c == '^';
        }

        // Note: nanoFramework strings are UTF-8 inside, so text[i] and text.Length walk the string from the
        // start (cost grows with the square of the length). We therefore work on a char array.
        private static byte[] EncodeUtf8(string text)
        {
            char[] chars = text.ToCharArray();

            int length = 0;
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                length += c < 0x80 ? 1 : (c < 0x800 ? 2 : 3);
            }

            byte[] result = new byte[length];
            int n = 0;
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c < 0x80)
                {
                    result[n++] = (byte)c;
                }
                else if (c < 0x800)
                {
                    result[n++] = (byte)(0xC0 | (c >> 6));
                    result[n++] = (byte)(0x80 | (c & 0x3F));
                }
                else
                {
                    result[n++] = (byte)(0xE0 | (c >> 12));
                    result[n++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                    result[n++] = (byte)(0x80 | (c & 0x3F));
                }
            }

            return result;
        }

        private static void PutU16(byte[] b, int offset, uint value)
        {
            b[offset] = (byte)value;
            b[offset + 1] = (byte)(value >> 8);
        }

        private static void PutU32(byte[] b, int offset, uint value)
        {
            b[offset] = (byte)value;
            b[offset + 1] = (byte)(value >> 8);
            b[offset + 2] = (byte)(value >> 16);
            b[offset + 3] = (byte)(value >> 24);
        }

        // ---------------------------------------------------------------- FAT and sectors

        private uint ClusterToSector(uint cluster)
        {
            return _dataStart + (cluster - 2) * _sectorsPerCluster;
        }

        // Next cluster of a chain (>= _endMark when it ends, 0 on read error)
        private uint NextCluster(uint cluster)
        {
            uint offset = _fat32 ? cluster * 4 : cluster * 2;
            uint sector = _fatStart + offset / 512;
            int index = (int)(offset % 512);

            if (!LoadFatSector(sector))
            {
                return 0;
            }

            if (_fat32)
            {
                return U32(_fatBuf, index) & 0x0FFFFFFF;
            }

            return U16(_fatBuf, index);
        }

        // Makes _fatBuf hold the given FAT sector (writes the previous one back first if it was changed)
        private bool LoadFatSector(uint sector)
        {
            if (sector == _fatCacheSector)
            {
                return true;
            }

            if (!FlushFat())
            {
                return false;
            }

            _fatCacheSector = 0xFFFFFFFF;
            if (!ReadSector(sector, _fatBuf))
            {
                return false;
            }

            _fatCacheSector = sector;
            return true;
        }

        // Writes the cached FAT sector to every copy of the FAT, if it was changed
        private bool FlushFat()
        {
            if (!_fatDirty)
            {
                return true;
            }

            for (uint k = 0; k < _numFats; k++)
            {
                if (!WriteSector(_fatCacheSector + k * _fatSize, _fatBuf))
                {
                    return false;
                }
            }

            _fatDirty = false;
            return true;
        }

        private bool WriteSector(uint sector, byte[] buffer)
        {
            if (_card.WriteBlock(sector, buffer))
            {
                return true;
            }

            _error = "Ecriture du secteur " + sector + " : " + _card.ErrorMessage;
            return false;
        }

        private bool ReadSector(uint sector, byte[] buffer)
        {
            if (_card.ReadBlock(sector, buffer))
            {
                return true;
            }

            _error = "Lecture du secteur " + sector + " : " + _card.ErrorMessage;
            return false;
        }

        private bool CheckMounted()
        {
            if (_mounted)
            {
                return true;
            }

            _error = "Systeme de fichiers non monte (appeler Mount)";
            return false;
        }

        private static bool LooksLikeBootSector(byte[] b)
        {
            return (b[0] == 0xEB || b[0] == 0xE9) && U16(b, 11) == 512 && b[13] != 0;
        }

        private static uint U16(byte[] b, int offset)
        {
            return (uint)(b[offset] | (b[offset + 1] << 8));
        }

        private static uint U32(byte[] b, int offset)
        {
            return (uint)b[offset] | ((uint)b[offset + 1] << 8) | ((uint)b[offset + 2] << 16) | ((uint)b[offset + 3] << 24);
        }
    }
}
