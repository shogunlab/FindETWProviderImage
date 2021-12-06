﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;

namespace FindETWProviderImage
{
    internal class Program
    {
        private static readonly string Usage = "FindETWProviderImage.exe \"{provider-guid}\" \"<search_path>\"";

        private static string TargetGuid;
        private static byte[] ProviderGuidBytes;
        private static HashSet<string> TargetFiles = new();
        private static int TotalReferences = 0;

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine(Usage);
                return;
            }
            if (!Guid.TryParse(args[0], out _))
            {
                throw new ArgumentException("The GUID provided does not appear to be valid");
            }
            if (!Directory.Exists(args[1]) && !File.Exists(args[1]))
            {
                throw new FileNotFoundException("The target file or directory does not exist");
            }

            Stopwatch sw = Stopwatch.StartNew();

            TargetGuid = args[0];
            ProviderGuidBytes = Guid.Parse(args[0]).ToByteArray();
            string SearchRoot = args[1];

            // Check if the search target is a directory
            if ((File.GetAttributes(SearchRoot) & FileAttributes.Directory) == FileAttributes.Directory)
            {
                // Build the list of files
                TargetFiles = GetAllFiles(SearchRoot);
                Console.WriteLine($"Searching {TargetFiles.Count} files for {TargetGuid}...");

                Parallel.ForEach(TargetFiles, file => ParseFile(file, ProviderGuidBytes));
            }
            else
            {
                ParseFile(SearchRoot, ProviderGuidBytes);                
            }

            Console.WriteLine($"\nTotal References: {TotalReferences}");
            sw.Stop();
            Console.WriteLine($"Time Elapsed: {sw.ElapsedMilliseconds / 1000.0000} seconds");
        }

        static void ParseFile(string FilePath, byte[] ProviderGuid)
        {
            byte[] FileBytes = File.ReadAllBytes(FilePath);

            List<int> Offsets = BoyerMooreSearch(ProviderGuidBytes, FileBytes);

            if (Offsets.Count > 0)
            {
                Console.WriteLine($"\nTarget File: {FilePath}\n" +
                            $"GUID: {TargetGuid}\n" +
                            $"Found {Offsets.Count} references:");
                foreach (var Offset in Offsets)
                {
                    Console.WriteLine("  {0}) Offset: 0x{1:x} RVA: 0x{2:x}",
                        Offsets.IndexOf(Offset) + 1,
                        Offset,
                        OffsetToRVA(FileBytes, Offset));
                }

                TotalReferences += Offsets.Count;
            }
        }

        static List<int> BoyerMooreSearch(byte[] ProviderGuid, byte[] FileBytes)
        {
            List<int> Offsets = new();

            int[] Alphabet = new int[256];
            for (int i = 0; i < Alphabet.Length; i++) 
            {
                Alphabet[i] = ProviderGuid.Length; 
            }

            for (int i = 0; i < ProviderGuid.Length; i++)
            {
                Alphabet[ProviderGuid[i]] = ProviderGuid.Length - i - 1;
            }

            int Index = ProviderGuid.Length - 1;
            var lastByte = ProviderGuid.Last();

            while (Index < FileBytes.Length)
            {
                byte CheckByte = FileBytes[Index];
                if (FileBytes[Index] == lastByte)
                {
                    bool Found = true;
                    for (int j = ProviderGuid.Length - 2; j >= 0; j--)
                    {
                        if (FileBytes[Index - ProviderGuid.Length + j + 1] != ProviderGuid[j])
                        {
                            Found = false;
                            break;
                        }
                    }

                    if (!Found)
                    {
                        Index++;
                    }
                    else
                    {
                        Offsets.Add(Index - ProviderGuid.Length + 1);
                        Index++;
                    }
                        
                }
                else
                {
                    Index += Alphabet[CheckByte];
                }
            }
            
            return Offsets;
        }

        static int OffsetToRVA(byte[] FileBytes, int Offset)
        {
            MemoryStream stream = new(FileBytes);
            PEReader reader = new(stream);

            var SectionHeaders = reader.PEHeaders.SectionHeaders;
            foreach (SectionHeader Header in SectionHeaders)
            {
                if (Offset > Header.VirtualAddress && Offset < Header.VirtualAddress + Header.VirtualSize)
                {
                    return Offset + Header.VirtualAddress - Header.PointerToRawData;
                }
            }

            return Offset;
        }

        static HashSet<string> GetAllFiles(string SearchDirectory)
        {
            EnumerationOptions Options = new()
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
            };

            return Directory.EnumerateFiles(SearchDirectory, "*.*", Options)
                .Where(s => s.EndsWith(".dll") || s.EndsWith(".exe") || s.EndsWith(".sys")).ToHashSet<string>();
        }
    }
}
