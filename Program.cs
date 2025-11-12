using SharpCompress.Compressors.LZMA;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;



namespace AM5SMU
{
    class Program
    {
        static readonly string[] zipFileBlacklist =
        {
            "/",
            ".txt",
            ".ini",
            ".bat",
            ".exe"
        };

        static void Main(string[] args)
        {
            Console.Title = "AM5 SMU Checker v1.15";

            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            if (args.Length == 0)
            {
                return;
            }

            foreach (var arg in args)
            {
                if (!File.Exists(arg))
                {
                    continue;
                }

                try
                {
                    int w = Math.Min(76, Console.LargestWindowWidth);
                    int h = Math.Min(42, Console.LargestWindowHeight);
                    if (w > 0 && h > 0) Console.SetWindowSize(w, h);
                }
                catch { }

                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Log("                            U E F I   I N F O                              ", ConsoleColor.White);
                Console.ResetColor();

                string biosName = null;
                byte[] biosBytes = null;

                if (arg.EndsWith(".zip"))
                {
                    using (var zipArchive = ZipFile.OpenRead(arg))
                    {
                        foreach (var zipEntry in zipArchive.Entries)
                        {
                            if (string.IsNullOrEmpty(zipEntry.Name) ||
                                zipFileBlacklist.Any(x => zipEntry.Name.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            biosName = zipEntry.Name;

                            using (var byteStream = zipEntry.Open())
                            using (var memStream = new MemoryStream())
                            {
                                byteStream.CopyTo(memStream);
                                biosBytes = memStream.ToArray();
                            }
                            break;
                        }
                    }

                    if (biosName is null || biosBytes is null)
                    {
                        Log($"Could not retrieve bios from {arg}\n", ConsoleColor.DarkRed);
                        continue;
                    }
                }
                else
                {
                    biosName = Path.GetFileName(arg);
                    biosBytes = File.ReadAllBytes(arg);

                    //Log($"   File:    {biosName}");
                    //Log($"   Size:    {BytesToKB(biosBytes.Length).ToString("N0")} KB");

                    // Agesa, Name, Version, ... ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
                    try
                    {
                        var both = AgesaLzmaScanner.TryFindAgesaAndAmiInLzma(biosBytes);
                        if (both != null)
                        {
                            
                            string oem = (both.NameWords != null && both.NameWords.Length > 0) ? both.NameWords[0] : string.Empty;
                            string board = (both.NameWords != null && both.NameWords.Length > 1) ? both.NameWords[1] : string.Empty;
                            bool agesaFound = !string.IsNullOrEmpty(both.Agesa);
                            string agesa = $"AGESA {both.Agesa}";
                            int customWidth = 73;
                            int padoem = Math.Max(0, (customWidth - oem.Length) / 2);
                            int padBoard = Math.Max(0, (customWidth - board.Length) / 2);
                            int padAgesa = Math.Max(0, (customWidth - agesa.Length) / 2);
                            Log(new string(' ', padoem) + oem);
                            Log(new string(' ', padBoard) + board, ConsoleColor.DarkYellow);
                            if (agesaFound)
                            {
                                Log(new string(' ', padAgesa) + agesa, ConsoleColor.White);
                            }

                            Log("───────────────────────────────────────────────────────────────────────────", ConsoleColor.DarkGray);
                            Console.ResetColor();

                            string buildDate = ReformatAmiDateToDMY(both.AmiLastWord2);
                            if (!string.IsNullOrEmpty(buildDate) && buildDate.Contains("2012"))
                            {
                                buildDate = "N/A";
                            }
                            if (!string.IsNullOrEmpty(both.AmiLastWord1))
                            {
                                if (both.NameWords != null && both.NameWords.Length > 0)
                                {
                                    var firstName = both.NameWords[0];
                                    if (firstName.IndexOf("ASRock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        firstName.IndexOf("NZXT", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        var ver = ExtractVersionFromFileName(biosName);
                                        both.AmiLastWord1 = !string.IsNullOrEmpty(ver) ? ver : "N/A";
                                    }
                                }
                                Log("        UEFI Version            Build Date              File Size", ConsoleColor.Gray);
                                Log($"        {both.AmiLastWord1.PadRight(21)}   {buildDate.PadRight(21)}   {BytesToKB(biosBytes.Length).ToString("N0")} KB ");
                                Console.ResetColor();
                            }
                        }
                    }
                    catch
                    { }
                }



                // Chipsatz  ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
                var oChipx = SearchPattern(biosBytes, "5F 50 54 5F", 0x0);
                if (oChipx.Any())
                {
                    Log("───────────────────────────────────────────────────────────────────────────", ConsoleColor.DarkGray);
                    int foundCount = 0;
                    foreach (var smuOffset in oChipx)
                    {
                        if (smuOffset - 0x94 < 0 || smuOffset + 0x98 > biosBytes.Length)  
                            continue;

                        var span = biosBytes.AsSpan(smuOffset);
                        int smuLen = BitConverter.ToInt32(biosBytes, smuOffset - 0x94);

                        string date = $"20{span[0x8C]:X2}.{span[0x8D]:X2}.{span[0x8E]:X2}"; // 20YY.MM.DD                
                        string smuVer = $"{span[0x8F]:X2}.{span[0x90]:X2}.{span[0x91]:X2}"; // SMU-Version: AA.BB.CC
                        string fw = "FW" + Encoding.ASCII.GetString(biosBytes, smuOffset + 0x93, 5); // "FW" + 5 ASCII-Zeichen ab 0x93

                        Log($"        Chipset Info:   {smuVer} | {fw} | {date} | ({BytesToKB(smuLen):N0} KB)", ConsoleColor.Gray);

                        foundCount++;
                        if (foundCount >= 2)
                            break;
                    }
                }



                Console.Write(Environment.NewLine);
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Log("        S Y S T E M    M A N A G E M E N T    U N I T    [ S M U ]         ", ConsoleColor.White);
                Console.BackgroundColor = ConsoleColor.DarkCyan;
                Log("    Version        Size        CPU/APU  Family               Offset        ", ConsoleColor.Black);
                Console.ResetColor();


                // Raphael1 +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
                var Raphael = SearchPattern(biosBytes, "54 ? 00 00 00 00 00 00 00 ? ? ? ? 00 00 00 00 00 00 00 00 00 00 00 00 00 08 00 01 00", -0x62);
                if (Raphael.Any())
                {
                    foreach (var smuOffset in Raphael)
                    {
                        var smuLen = BitConverter.ToInt32(biosBytes, smuOffset + 0x6C);
                        var smuVer = $"{biosBytes[smuOffset + 0x63]}.{biosBytes[smuOffset + 0x62].ToString("00")}.{biosBytes[smuOffset + 0x61].ToString("00")}.{biosBytes[smuOffset + 0x60]}";

                        Log($"   {(smuVer).PadRight(11)}   ({BytesToKB(smuLen).ToString("N0").PadLeft(3, ' ')} KB) " +
                            $"  Raphael/X      7xx0 CPU   [{smuOffset.ToString("X").PadLeft(8, '0')}-{(smuOffset + smuLen).ToString("X").PadLeft(8, '0')}]", ConsoleColor.Green);
                    }
                }
                else
                {
                    byte[] rpl1 = { 0x12, 0x60, 0x0A, 0x05, 0x80 };
                    byte[] mask1 = { 0xFF, 0xFF, 0xFF, 0x00, 0xFF };

                    byte[] rpl2 = { 0x13, 0x60, 0x0A, 0x05, 0x80 };
                    byte[] mask2 = { 0xFF, 0xFF, 0xFF, 0x00, 0xFF };

                    if (ContainsSequenceMasked(biosBytes, rpl1, mask1) || ContainsSequenceMasked(biosBytes, rpl2, mask2))

                    {
                        Log($"   Found Raphael CPUID but SMU detection failed\n   Program update may be necessary", ConsoleColor.DarkGray);
                    }
                    else
                    {
                        Log($"   Could't find any Raphael SMU or CPUID - CPU may not be supported", ConsoleColor.DarkGray);
                    }
                }

                // Phoenix ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
                var Phoenix = SearchPattern(biosBytes, "01 00 00 00 02 00 00 00 00 00 04 00 ? ? ? 00 ? 10 01 81 00 00 00 00 ? ? 4C", -0x48);
                if (Phoenix.Any())
                {
                    Log("   ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─", ConsoleColor.DarkGray);
                    foreach (var smuOffset in Phoenix)
                    {
                        var smuLen = BitConverter.ToInt32(biosBytes, smuOffset + 0x6C);
                        var smuVer = $"{biosBytes[smuOffset + 0x63]}.{biosBytes[smuOffset + 0x62].ToString("00")}.{biosBytes[smuOffset + 0x61].ToString("00")}.{biosBytes[smuOffset + 0x60]}";

                        Log($"   {(smuVer).PadRight(11)}   ({BytesToKB(smuLen).ToString("N0").PadLeft(3, ' ')} KB) " +
                            $"  Phoenix/2      8xx0 APU   [{smuOffset.ToString("X").PadLeft(8, '0')}-{(smuOffset + smuLen).ToString("X").PadLeft(8, '0')}]", ConsoleColor.Green);
                    }
                }
                else
                {
                    byte[] phx1 = { 0x52, 0x70, 0x0A, 0x05, 0x80 };
                    byte[] mask1 = { 0xFF, 0xFF, 0xFF, 0x00, 0xFF };

                    byte[] phx2 = { 0x80, 0x70, 0x0A, 0x05, 0x80 };
                    byte[] mask2 = { 0xFF, 0xFF, 0xFF, 0x00, 0xFF };

                    if (ContainsSequenceMasked(biosBytes, phx1, mask1) || ContainsSequenceMasked(biosBytes, phx2, mask2))
                    {
                        Log("   ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─", ConsoleColor.DarkGray);
                        Log($"   Found Phoenix CPUID but SMU detection failed\n   Program update may be necessary", ConsoleColor.DarkGray);
                    }
                    else
                    {
                        Log("   ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─", ConsoleColor.DarkGray);
                        Log($"   Could't find any Phoenix SMU or CPUID - APU may not be supported", ConsoleColor.DarkGray);
                    }
                }

                // Granite Ridge ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
                var Granite = SearchPattern(biosBytes, "62 00 00 00 00 00 00 00 00 ? ? ? ? 00 00 00 00 00 00 00 00 00 00 00 00 00 08 00 01 00", -0x62);
                if (Granite.Any())
                {
                    Log("   ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─", ConsoleColor.DarkGray);
                    foreach (var smuOffset in Granite)
                    {
                        var smuLen = BitConverter.ToInt32(biosBytes, smuOffset + 0x6C);
                        var smuVer = $"{biosBytes[smuOffset + 0x63]}.{biosBytes[smuOffset + 0x62].ToString("00")}.{biosBytes[smuOffset + 0x61].ToString("00")}.{biosBytes[smuOffset + 0x60]}";

                        Log($"   {(smuVer).PadRight(11)}   ({BytesToKB(smuLen).ToString("N0").PadLeft(3, ' ')} KB) " +
                            $"  Granite Ridge  9xx0 CPU   [{smuOffset.ToString("X").PadLeft(8, '0')}-{(smuOffset + smuLen).ToString("X").PadLeft(8, '0')}]", ConsoleColor.Green);
                    }
                }
                else
                {
                    byte[] gnr1 = { 0x40, 0x40, 0x0B, 0x15, 0x80 };
                    byte[] mask1 = { 0xFF, 0xFF, 0xFF, 0x00, 0xFF };

                    byte[] gnr2 = { 0x41, 0x40, 0x0B, 0x15, 0x80 };
                    byte[] mask2 = { 0xFF, 0xFF, 0xFF, 0x00, 0xFF };

                    if (ContainsSequenceMasked(biosBytes, gnr1, mask1) || ContainsSequenceMasked(biosBytes, gnr2, mask2))
         
                    {
                        Log("   ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─", ConsoleColor.DarkGray);
                        Log($"   Found Granite Ridge CPUID but SMU detection failed\n   Program update may be necessary", ConsoleColor.DarkGray);
                    }
                    else
                    {
                        Log("   ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─", ConsoleColor.DarkGray);
                        Log($"   Could't find any Granite Ridge SMU or CPUID - CPU may not be supported", ConsoleColor.DarkGray);
                    }
                }








                Console.Write(Environment.NewLine);
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Log("                 Credits to RaINi, Reous and PatrickSchur                  ", ConsoleColor.Black);
                Console.ResetColor();
            }
            //Log("", wait: true);
            Console.ReadLine();
        }

        static double BytesToKB(int bytes)
        {
            return bytes / 1024d;
        }

        static void Log(string message, ConsoleColor color = ConsoleColor.White, bool newLine = true, bool wait = false)
        {
            Console.ForegroundColor = color;
            Console.Write(newLine ? message + Environment.NewLine : message);
            Console.ResetColor();

            if (wait)
            {
                Console.ReadLine();
            }
        }

        static int[] CreateMatchingsTable((byte, bool)[] patternTuple)
        {
            var skipTable = new int[256];
            var wildcards = patternTuple.Select(x => x.Item2).ToArray();
            var lastIndex = patternTuple.Length - 1;

            var diff = lastIndex - Math.Max(Array.LastIndexOf(wildcards, false), 0);
            if (diff == 0)
            {
                diff = 1;
            }

            for (var i = 0; i < skipTable.Length; i++)
            {
                skipTable[i] = diff;
            }

            for (var i = lastIndex - diff; i < lastIndex; i++)
            {
                skipTable[patternTuple[i].Item1] = lastIndex - i;
            }

            return skipTable;
        }

        static List<int> SearchPattern(byte[] data, string pattern, int offset = 0x0)
        {
            if (!data.Any() || string.IsNullOrEmpty(pattern))
            {
                throw new ArgumentException("Data or Pattern is empty");
            }

            var patternTuple = pattern.Split(' ')
                .Select(hex => hex.Contains('?')
                    ? (byte.MinValue, false)
                    : (Convert.ToByte(hex, 16), true))
                .ToArray();

            if (!patternTuple.Any())
            {
                throw new Exception("Failed to parse Pattern");
            }

            if (data.Length < patternTuple.Length)
                throw new ArgumentException("Data cannot be smaller than the Pattern");
        

            var lastPatternIndex = patternTuple.Length - 1;
            var skipTable = CreateMatchingsTable(patternTuple);
            var adressList = new List<int>();

            for (var i = 0; i <= data.Length - patternTuple.Length; i += Math.Max(skipTable[data[i + lastPatternIndex] & 0xFF], 1))
            {
                for (var j = lastPatternIndex; !patternTuple[j].Item2 || data[i + j] == patternTuple[j].Item1; --j)
                {
                    if (j == 0)
                    {
                        adressList.Add(i + offset);
                        break;
                    }
                }
            }
            return adressList;
        }

        static string ExtractVersionFromFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;

            var name = Path.GetFileNameWithoutExtension(fileName);

            // Optional dritte numerische Gruppe sowie optionales .AS##
            var matches = Regex.Matches(
                name,
                @"\d+\.\d+(?:\.\d+)?(?:\.[A-Z]{2}\d{2})?",
                RegexOptions.IgnoreCase
            );

            if (matches.Count == 0) return null;
            return matches[matches.Count - 1].Value;
        }


        static string ReformatAmiDateToDMY(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "N/A";

            input = input.Trim();

            // 1) Versuch: als US-Datum (MM/DD/YYYY) parsen
            string[] formats = new[]
            {
        "M/d/yyyy", "MM/dd/yyyy",
        "M/d/yy", "MM/dd/yy",
        "M-d-yyyy", "MM-dd-yyyy",
        "M.d.yyyy", "MM.dd.yyyy",
        "M/d/yyyy HH:mm:ss", "MM/dd/yyyy HH:mm:ss",
        "M/d/yyyy h:mm tt", "MM/dd/yyyy h:mm tt"
    };

            if (DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
            {
                // Ausgabe mit Punkten statt Schrägstrichen
                return dt.ToString("dd.MM.yyyy");
            }

            // 2) Fallback: numerische Teile extrahieren
            var parts = Regex.Split(input, @"\D+").Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (parts.Length >= 3)
            {
                if (int.TryParse(parts[0], out int month) &&
                    int.TryParse(parts[1], out int day) &&
                    int.TryParse(parts[2], out int year))
                {
                    if (year < 100) year += (year >= 0 && year <= 50) ? 2000 : 1900;

                    // Normaler US-Fall: Monat/Tag/Jahr → umdrehen
                    if (day >= 1 && day <= 31 && month >= 1 && month <= 12)
                        return $"{day:D2}.{month:D2}.{year:D4}";

                    // Falls vertauscht (selten)
                    if (month >= 1 && month <= 31 && day >= 1 && day <= 12)
                        return $"{month:D2}.{day:D2}.{year:D4}";
                }
            }

            // 3) Wenn gar nichts passt → "N/A"
            return "N/A";
        }
        static bool ContainsSequenceMasked(byte[] data, byte[] pattern, byte[] mask)
        {
            if (data == null || pattern == null || mask == null ||
                pattern.Length == 0 || data.Length < pattern.Length ||
                pattern.Length != mask.Length)
                return false;

            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (mask[j] == 0x00) continue; // dieses Byte ignorieren
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true; // erstes Vorkommen -> fertig
            }
            return false;
        }



        internal sealed class FoundAndStopException : Exception
        {
            public string Result { get; }
            public FoundAndStopException(string result) : base(result) => Result = result;
        }

        internal sealed class ThrottledArraySegmentStream : Stream
        {
            private readonly byte[] _data;
            private readonly int _start;
            private readonly int _end;
            private readonly int _chunkSize;
            private int _pos;

            public ThrottledArraySegmentStream(byte[] data, int start, int? length = null, int chunkSize = 16384)
            {
                if (data == null) throw new ArgumentNullException(nameof(data));
                if (start < 0 || start > data.Length) throw new ArgumentOutOfRangeException(nameof(start));
                _data = data;
                _start = start;
                _pos = start;
                _end = length.HasValue ? Math.Min(start + length.Value, data.Length) : data.Length;
                _chunkSize = Math.Max(1, chunkSize);
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _end - _start;
            public override long Position { get => _pos - _start; set => Seek(value, SeekOrigin.Begin); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (buffer == null) throw new ArgumentNullException(nameof(buffer));
                if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();
                if (_pos >= _end) return 0;
                int remaining = _end - _pos;
                int toCopy = Math.Min(Math.Min(count, remaining), _chunkSize);
                Buffer.BlockCopy(_data, _pos, buffer, offset, toCopy);
                _pos += toCopy;
                return toCopy;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                long abs;
                switch (origin)
                {
                    case SeekOrigin.Begin: abs = _start + offset; break;
                    case SeekOrigin.Current: abs = _pos + offset; break;
                    case SeekOrigin.End: abs = _end + offset; break;
                    default: throw new ArgumentOutOfRangeException(nameof(origin));
                }
                if (abs < _start || abs > _end) throw new IOException("Seek out of range.");
                _pos = (int)abs;
                return _pos - _start;
            }

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        internal sealed class CombinedResult
        {
            public string Agesa { get; set; }
            public string AmiLastWord1 { get; set; }
            public string AmiLastWord2 { get; set; }
            public string[] NameWords { get; set; }   // neue Liste für C-Wörter
        }

        internal sealed class CombinedFinderSink : Stream
        {
            private static readonly byte[] PatAgesa = Encoding.ASCII.GetBytes("AGESA!V9");
            private static readonly byte[] PatAmi = { 0x41, 0x6D, 0x65, 0x72, 0x69, 0x63, 0x61, 0x6E, 0x20, 0x4D, 0x65, 0x67, 0x61, 0x74, 0x72, 0x65, 0x6E, 0x64, 0x73, 0x20 };
            private static readonly byte[] PatName = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x09, 0x06, 0xFF, 0x00, 0x0A, 0x00 };

            public readonly CombinedResult Result = new CombinedResult();

            // ---- AGESA ----
            private int _agMatch; private bool _agFound; private int _agStrIndex;
            private byte[] _agBuf = new byte[64]; private int _agLen; private bool _agDone;

            // ---- AMI ----
            private int _amMatch; private bool _amFound; private int _amZeroRun;
            private byte[] _amCur = new byte[64]; private int _amCurLen;
            private string _amLast1 = string.Empty, _amLast2 = string.Empty; private bool _amDone;

            // ---- NAME ----
            private int _nmMatch; private bool _nmFound; private int _nmZeroRun;
            private byte[] _nmCur = new byte[64]; private int _nmCurLen;
            private System.Collections.Generic.List<string> _nmWords = new System.Collections.Generic.List<string>();
            private bool _nmDone;

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
            {
                int end = offset + count;
                for (int i = offset; i < end; i++)
                {
                    byte b = buffer[i];

                    // ---- AGESA ----
                    if (!_agDone)
                    {
                        if (!_agFound)
                        {
                            if (b == PatAgesa[_agMatch])
                            {
                                _agMatch++;
                                if (_agMatch == PatAgesa.Length)
                                {
                                    _agFound = true; _agMatch = 0; _agStrIndex = 0; _agLen = 0;
                                }
                            }
                            else _agMatch = (b == PatAgesa[0]) ? 1 : 0;
                        }
                        else
                        {
                            if (b == 0x00)
                            {
                                if (_agStrIndex == 1)
                                {
                                    Result.Agesa = Encoding.ASCII.GetString(_agBuf, 0, _agLen);
                                    _agDone = true;
                                }
                                _agStrIndex++; _agLen = 0;
                            }
                            else if (_agStrIndex <= 1)
                            {
                                if (_agLen == _agBuf.Length)
                                {
                                    var nb = new byte[_agBuf.Length << 1];
                                    Buffer.BlockCopy(_agBuf, 0, nb, 0, _agBuf.Length);
                                    _agBuf = nb;
                                }
                                _agBuf[_agLen++] = b;
                            }
                        }
                    }

                    // ---- AMI ----
                    if (!_amDone)
                    {
                        if (!_amFound)
                        {
                            if (b == PatAmi[_amMatch])
                            {
                                _amMatch++;
                                if (_amMatch == PatAmi.Length)
                                {
                                    _amFound = true; _amMatch = 0;
                                    _amZeroRun = 0; _amCurLen = 0;
                                    _amLast1 = _amLast2 = string.Empty;
                                }
                            }
                            else _amMatch = (b == PatAmi[0]) ? 1 : 0;
                        }
                        else
                        {
                            if (b == 0x00)
                            {
                                _amZeroRun++;
                                if (_amCurLen > 0)
                                {
                                    var w = Encoding.ASCII.GetString(_amCur, 0, _amCurLen);
                                    if (!string.IsNullOrEmpty(w))
                                    { _amLast2 = _amLast1; _amLast1 = w; }
                                    _amCurLen = 0;
                                }
                                if (_amZeroRun >= 2)
                                {
                                    Result.AmiLastWord1 = _amLast2;
                                    Result.AmiLastWord2 = _amLast1;
                                    _amDone = true;
                                }
                            }
                            else
                            {
                                _amZeroRun = 0;
                                if (_amCurLen == _amCur.Length)
                                {
                                    var nb = new byte[_amCur.Length << 1];
                                    Buffer.BlockCopy(_amCur, 0, nb, 0, _amCur.Length);
                                    _amCur = nb;
                                }
                                _amCur[_amCurLen++] = b;
                            }
                        }
                    }

                    // ---- NAME ----
                    if (!_nmDone)
                    {
                        if (!_nmFound)
                        {
                            //if (b == PatName[_nmMatch])
                            if (PatName[_nmMatch] == 0xFF || b == PatName[_nmMatch])
                            {
                                _nmMatch++;
                                if (_nmMatch == PatName.Length)
                                {
                                    _nmFound = true;
                                    _nmMatch = 0;
                                    _nmZeroRun = 0;
                                    _nmCurLen = 0;
                                    _nmWords.Clear();
                                }
                            }
                            else
                            {
                                _nmMatch = (b == PatName[0]) ? 1 : 0;
                            }
                        }
                        else
                        {
                            // Nach Pattern: C-Strings lesen
                            if (b == 0x00)
                            {
                                _nmZeroRun++;

                                if (_nmCurLen > 0)
                                {
                                    var w = Encoding.ASCII.GetString(_nmCur, 0, _nmCurLen);
                                    if (!string.IsNullOrEmpty(w))
                                        _nmWords.Add(w);

                                    _nmCurLen = 0;
                                }

                                // Wenn 2 Wörter gesammelt → abbrechen
                                if (_nmWords.Count >= 2)
                                {
                                    //Result.NameWords = new[] { _nmWords[1] }; // nur das zweite Wort
                                    Result.NameWords = _nmWords.ToArray();
                                    _nmDone = true;
                                }
                            }
                            else
                            {
                                _nmZeroRun = 0;
                                if (_nmCurLen == _nmCur.Length)
                                {
                                    var nb = new byte[_nmCur.Length << 1];
                                    Buffer.BlockCopy(_nmCur, 0, nb, 0, _nmCur.Length);
                                    _nmCur = nb;
                                }
                                _nmCur[_nmCurLen++] = b;
                            }
                        }
                    }

                    // alle drei fertig → abbrechen
                    if (_agDone && _amDone && _nmDone)
                        throw new FoundAndStopException("all");
                }
            }
        }

        internal static class AgesaLzmaScanner
        {
            // 16-Byte-GUID für den AMI-/AGESA-LZMA-Block (Little-Endian Bytefolge)
            private static readonly byte[] GuidBytes = {0x93, 0xFD, 0x21, 0x9E, 0x72, 0x9C, 0x15, 0x4C, 0x8C, 0x4B, 0xE7, 0x7F, 0x1D, 0xB2, 0xD7, 0x92};

            public static CombinedResult TryFindAgesaAndAmiInLzma(byte[] bios)
            {
                if (bios == null || bios.Length < 32) return null;

                int guidAt = IndexOfGuidBMH(bios, GuidBytes);
                if (guidAt < 0) return null;

                // bevorzugte Offsets nach der GUID: erst 0x30, dann 0x3C
                int[] relOffsets = { 0x30, 0x3C };

                foreach (int rel in relOffsets)
                {
                    int lzmaStart = guidAt + rel;
                    // Mind. 13 Header-Bytes (5 Props + 8 Size) müssen vorhanden sein
                    if (lzmaStart < 0 || lzmaStart + 13 > bios.Length) continue;

                    using (var src = new ThrottledArraySegmentStream(bios, lzmaStart, null, 4096))
                    {
                        // --- LZMA-Alone Header: 5 Props + 8 UncompressedSize (konsumieren!) ---
                        byte[] props = new byte[5];
                        if (src.Read(props, 0, 5) != 5) continue;

                        byte[] sizeBuf = new byte[8];
                        if (src.Read(sizeBuf, 0, 8) != 8) continue;

                        long outSize;
                        bool allFF = true;
                        for (int i = 0; i < 8; i++) if (sizeBuf[i] != 0xFF) { allFF = false; break; }
                        if (allFF)
                            outSize = -1;
                        else
                        {
                            ulong u = BitConverter.ToUInt64(sizeBuf, 0);
                            outSize = (u <= long.MaxValue) ? (long)u : -1;
                        }

                        try
                        {
                            using (var lz = new LzmaStream(props, src, -1, outSize))
                            {
                                var sink = new CombinedFinderSink();
                                byte[] buf = new byte[8192];
                                int r;

                                try
                                {
                                    while ((r = lz.Read(buf, 0, buf.Length)) > 0)
                                        sink.Write(buf, 0, r);
                                }
                                catch (FoundAndStopException)
                                {
                                    return sink.Result;
                                }

                                // Kein Frühabbruch – wenn wenigstens etwas gefunden wurde, akzeptieren
                                if (!string.IsNullOrEmpty(sink.Result?.Agesa) ||
                                    !string.IsNullOrEmpty(sink.Result?.AmiLastWord1) ||
                                    !string.IsNullOrEmpty(sink.Result?.AmiLastWord2) ||
                                    (sink.Result?.NameWords != null && sink.Result.NameWords.Length > 0))
                                {
                                    return sink.Result;
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                return null;
            }

            // Boyer-Moore-Horspool für die 16-Byte-GUID (schnell)
            private static int IndexOfGuidBMH(byte[] data, byte[] needle)
            {
                int n = data.Length, m = needle.Length;
                if (m == 0) return 0;
                if (n < m) return -1;

                int[] shift = new int[256];
                for (int i = 0; i < shift.Length; i++) shift[i] = m;
                for (int i = 0; i < m - 1; i++) shift[needle[i]] = m - 1 - i;

                int pos = 0;
                byte last = needle[m - 1];
                while (pos <= n - m)
                {
                    if (data[pos + m - 1] == last)
                    {
                        int j = m - 2;
                        while (j >= 0 && data[pos + j] == needle[j]) j--;
                        if (j < 0) return pos;
                    }
                    pos += shift[data[pos + m - 1]];
                }
                return -1;
            }
        }





    }
}
