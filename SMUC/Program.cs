using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;


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
            Console.Title = "AM5 SMU Checker v0.8";

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

                Console.SetWindowSize(80, 42); //Breite, Höhe52

                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Log("                            U E F I   I N F O                             ", ConsoleColor.White);
                Console.ResetColor();

                string biosName = null;
                byte[] biosBytes = null;

                if (arg.EndsWith(".zip"))
                {
                    using (var zipArchive = ZipFile.OpenRead(arg))
                    {
                        foreach (var zipEntry in zipArchive.Entries)
                        {
                            if (string.IsNullOrEmpty(zipEntry.Name) || zipFileBlacklist.Any(x => zipEntry.Name.EndsWith(x)))
                            {
                                continue;
                            }

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
                    
                Log($"   File:  {biosName}");
                Log($"   Size:  {BytesToKB(biosBytes.Length).ToString("N0")} KB");
                }
                  


                var agesaVersion = SearchPattern(biosBytes, "3D 9B 25 70 41 47 45 53 41", 0xD)
                    .FirstOrDefault();
                if (agesaVersion != 0)
                {
                    var buf = new byte[255];
                    Array.Copy(biosBytes, agesaVersion, buf, 0, buf.Length);

                    var versionStr = Encoding.UTF8.GetString(buf);
                    if (versionStr.Contains('\0'))
                    {
                        versionStr = versionStr.Substring(0, versionStr.IndexOf('\0'));
                    }

                    Log($"   AGESA: {versionStr}");
                }

                Console.Write(Environment.NewLine);

                // Other Chipsatz
                var OChip = SearchPattern(biosBytes, "5F 50 54 5F", 0x0);
                    if (OChip.Any())
                    {
                        foreach (var smuOffset in OChip)
                        {
                            var smuLen = BitConverter.ToInt32(biosBytes, smuOffset -0x94);

                            string Date1 = "";
                            for (int i = smuOffset + 0x8C; i < smuOffset + 0x8E; i++)
                            {
                                Date1 += BitConverter.ToString(biosBytes, i, 1) + ".";
                            }
                            string Date2 = "";
                            for (int i = smuOffset + 0x8E; i < smuOffset + 0x8F; i++)
                            {
                            Date1 += BitConverter.ToString(biosBytes, i, 1) + "";
                            }

                            string FW = "FW";
                            for (int i = smuOffset + 0x93; i < smuOffset + 0x98; i++)
                            {
                                FW += Encoding.UTF8.GetString(biosBytes, i, 1);
                            }

                            string SMU1 = "";
                            for (int i = smuOffset + 0x8F; i < smuOffset + 0x91; i++)
                            {
                                SMU1 += BitConverter.ToString(biosBytes, i, 1) + ".";
                            }
                            string SMU2 = "";
                            for (int i = smuOffset + 0x91; i < smuOffset + 0x92; i++)
                            {
                                SMU2 += BitConverter.ToString(biosBytes, i, 1);
                            }

                            Log($"   Chipset Version/FW:    {SMU1}{SMU2} | {FW} | 20{Date1}{Date2} | ({BytesToKB(smuLen).ToString("N0").PadLeft(3, ' ')} KB)", ConsoleColor.Gray);
                        }
                    }
                
                Console.Write(Environment.NewLine);
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Log("        S Y S T E M    M A N A G E M E N T    U N I T    [ S M U ]        ", ConsoleColor.White);
                Console.BackgroundColor = ConsoleColor.DarkCyan;
                Log("   Version       Size          CPU/APU  Family            Offset          ", ConsoleColor.Black);
                Console.ResetColor();


                // Raphael1 //
                var Raphael = SearchPattern(biosBytes, "54 00 00 00 00 00 00 00 00 ? ? ? ? 00 00 00 00 00 00 00 00 00 00 00 00 00 08 00 01 00", -0x62);
                if (Raphael.Any())
                {
                    foreach (var smuOffset in Raphael)
                    {
                        var smuLen = BitConverter.ToInt32(biosBytes, smuOffset + 0x6C);
                        var smuVer = $"{biosBytes[smuOffset + 0x62]}.{biosBytes[smuOffset + 0x61]}.{biosBytes[smuOffset + 0x60]}";

                        Log($"   {(smuVer).PadRight(9)}   ({BytesToKB(smuLen).ToString("N0").PadLeft(3, ' ')} KB) " +
                            $"  Raphael/X      7xx0 CPU   [{smuOffset.ToString("X").PadLeft(8, '0')}-{(smuOffset + smuLen).ToString("X").PadLeft(8, '0')}]", ConsoleColor.Green);

                    }
                }

                // Phoenix //
                var Phoenix = SearchPattern(biosBytes, "01 00 00 00 02 00 00 00 00 00 04 00 ? ? ? 00 ? 10 01 81 00 00 00 00 ? ? 4C", -0x48);
                if (Phoenix.Any())
                {

                    Log("   ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ", ConsoleColor.Cyan);
                    foreach (var smuOffset in Phoenix)
                    {
                        var smuLen = BitConverter.ToInt32(biosBytes, smuOffset + 0x6C);
                        var smuVer = $"{biosBytes[smuOffset + 0x62]}.{biosBytes[smuOffset + 0x61]}.{biosBytes[smuOffset + 0x60]}";

                        Log($"   {(smuVer).PadRight(9)}   ({BytesToKB(smuLen).ToString("N0").PadLeft(3, ' ')} KB) " +
                            $"  Phoenix/2      8xx0 APU   [{smuOffset.ToString("X").PadLeft(8, '0')}-{(smuOffset + smuLen).ToString("X").PadLeft(8, '0')}]", ConsoleColor.Green);
                    }
                }

                // Granite Ridge //
                var Granite = SearchPattern(biosBytes, "62 00 00 00 00 00 00 00 00 ? ? ? ? 00 00 00 00 00 00 00 00 00 00 00 00 00 08 00 01 00", -0x62);
                if (Granite.Any())
                {

                    Log("   ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ", ConsoleColor.Cyan);
                    foreach (var smuOffset in Granite)
                    {
                        var smuLen = BitConverter.ToInt32(biosBytes, smuOffset + 0x6C);
                        var smuVer = $"{biosBytes[smuOffset + 0x62]}.{biosBytes[smuOffset + 0x61]}.{biosBytes[smuOffset + 0x60]}";

                        Log($"   {(smuVer).PadRight(9)}   ({BytesToKB(smuLen).ToString("N0").PadLeft(3, ' ')} KB) " + 
                            $"  Granite Ridge  9xx0 CPU   [{smuOffset.ToString("X").PadLeft(8, '0')}-{(smuOffset + smuLen).ToString("X").PadLeft(8, '0')}]", ConsoleColor.Green);
                    }
                }
           

                Console.Write(Environment.NewLine);
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Log("                 Credits to RaINi, Reous and PatrickSchur                 ", ConsoleColor.Black);
                Console.ResetColor();
            }
            Log("", wait: true);
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

            if (data.Length < pattern.Length)
            {
                throw new ArgumentException("Data cannot be smaller than the Pattern");
            }

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
    }
}
