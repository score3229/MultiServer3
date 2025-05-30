using SimdJsonSharp;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

#if NETCOREAPP3_0_OR_GREATER
using System.Runtime.Intrinsics.X86;
#else
using System.Runtime.InteropServices;
#endif
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NetworkLibrary.Extension
{
    public static class StringUtils
    {
#if !NETCOREAPP3_0_OR_GREATER
        private const int PF_AVX2_INSTRUCTIONS_AVAILABLE = 40;

        [DllImport("kernel32.dll")]
        private static extern bool IsProcessorFeaturePresent(int processorFeature);
#endif

        public static string ChopOffBefore(this string s, string Before)
        {
            // Usefull function for chopping up strings
            int End = s.ToUpper().IndexOf(Before.ToUpper());
            if (End > -1)
                return s.Substring(End + Before.Length);

            return s;
        }

        public static string ChopOffAfter(this string s, string After)
        {
            // Usefull function for chopping up strings
            int End = s.ToUpper().IndexOf(After.ToUpper());
            if (End > -1)
                return s.Substring(0, End);
            return s;
        }

        public static string ReplaceIgnoreCase(this string Source, string Pattern, string Replacement)
        {
            // using \\$ in the pattern will screw this regex up
            // return Regex.Replace(Source, Pattern, Replacement, RegexOptions.IgnoreCase);

            if (Regex.IsMatch(Source, Pattern, RegexOptions.IgnoreCase))
                Source = Regex.Replace(Source, Pattern, Replacement, RegexOptions.IgnoreCase);

            return Source;
        }

        public static string TrimBeforeExtension(this string fileName)
        {
            return Path.GetFileNameWithoutExtension(fileName).Trim() + Path.GetExtension(fileName);
        }

        public static string GetSubstringByString(this string a, string b, string c)
        {
            return c.Substring(c.IndexOf(a), c.IndexOf(b) - c.IndexOf(a));
        }

        /// <summary>
        /// Transform a string to it's hexadecimal representation.
        /// <para>Obtenir un string dans sa représentation hexadecimale.</para>
        /// <param name="str">The string to transform.</param>
        /// </summary>
        /// <returns>A string.</returns>
        public static string ToHexString(this string str)
        {
            StringBuilder sb = new StringBuilder();

            foreach (byte t in Encoding.UTF8.GetBytes(str))
            {
                sb.Append(t.ToString("X2"));
            }

            return sb.ToString();
        }

        public static string HexStringToString(this string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];

            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return Encoding.UTF8.GetString(bytes);
        }

        public static double Eval(this string expression, string filter = null)
        {
            return Convert.ToDouble(new DataTable().Compute(expression, filter));
        }

        /// <summary>
        /// Convert a hex-formatted string to byte array.
        /// <para>Convertir une représentation hexadécimal en tableau de bytes.</para>
        /// </summary>
        /// <param name="hex">A string looking like "300D06092A864886F70D0101050500".</param>
        /// <returns>A byte array.</returns>
        public static byte[] HexStringToByteArray(this string hex)
        {
            string cleanedRequest = hex.Replace(" ", string.Empty).Replace("\n", string.Empty);

            if (cleanedRequest.Length % 2 == 1)
                throw new Exception("[StringUtils] - HexStringToByteArray - The binary key cannot have an odd number of digits");

            byte[] arr = new byte[cleanedRequest.Length >> 1];

            for (int i = 0; i < cleanedRequest.Length >> 1; ++i)
            {
                arr[i] = (byte)((cleanedRequest[i << 1].GetHexVal() << 4) + cleanedRequest[(i << 1) + 1].GetHexVal());
            }

            return arr;
        }

        /// <summary>
        /// Converts a Ghidra string report into a byte array.
        /// Extracts hex bytes from each line and places them at the correct index.
        /// </summary>
        /// <param name="report">The Ghidra report as a string</param>
        /// <returns>Byte array constructed from the report</returns>
        public static byte[] GhidraStrReportToBytes(string report, int sizeOfOutput = -1)
        {
            string[] lines = report.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            byte[] byteArray = new byte[sizeOfOutput <= -1 ? lines.Length : sizeOfOutput];

            foreach (string line in lines)
            {
                Match match = Regex.Match(line, @"\b([0-9a-fA-F]+)\s+([0-9a-fA-F]{2})\s+.*?\[(\d+)\]");
                if (match.Success)
                    byteArray[int.Parse(match.Groups[3].Value)] = Convert.ToByte(match.Groups[2].Value, 16);
            }

            return byteArray;
        }

        /// <summary>
        /// Verify if the string is in base64 format.
        /// <para>Vérifie si un string est en format base64.</para>
        /// </summary>
        /// <param name="base64String">The base64 string.</param>
        /// <returns>A tuple boolean, byte array.</returns>
        public static (bool, byte[]) IsBase64(this string base64String)
        {
            if (string.IsNullOrEmpty(base64String))
                return (false, null);

#if NETCOREAPP2_1_OR_GREATER
            Span<byte> buffer = new byte[((base64String.Length * 3) + 3) / 4 -
                (base64String.Length > 0 && base64String[^1] == '=' ?
                    base64String.Length > 1 && base64String[^2] == '=' ?
                        2 : 1 : 0)];

            if (Convert.TryFromBase64String(base64String, buffer, out int bytesWritten))
                return (true, buffer[..bytesWritten].ToArray());
#else
            try
            {
                return (true, Convert.FromBase64String(base64String));
            }
            catch
            {
            }
#endif
            char[] base64CharArray = base64String.Replace(" ", string.Empty)
                    .Replace("\t", string.Empty).Replace("\r", string.Empty)
                    .Replace("\n", string.Empty)
                    .ToCharArray();
            try
            {
                // Fallback to managed implementation (NET's own decoder has issues with python base64 data)
                Decoded managedDecodeResult = ManagedBase64.Decode(base64CharArray);
                bool hasDecoded = managedDecodeResult.success;
                if (!hasDecoded)
                {
                    managedDecodeResult = ManagedBase64.Decode(base64CharArray, !hasDecoded);
                    hasDecoded = managedDecodeResult.success;
                    return (hasDecoded, hasDecoded ?
                        ManagedBase64.NumberArrayToString(managedDecodeResult.data)
                        .SelectMany(c =>
                            {
                                byte[] charBytes = BitConverter.GetBytes(c);
                                if (!BitConverter.IsLittleEndian)
                                    Array.Reverse(charBytes);
                                return charBytes;
                            })
                        .Where((_, index) => index % 2 == 0)
                        .ToArray()
                        : null);
                }
                else
                    return (hasDecoded, ManagedBase64.NumberArrayToString(managedDecodeResult.data)
                        .SelectMany(c =>
                        {
                            byte[] charBytes = BitConverter.GetBytes(c);
                            if (!BitConverter.IsLittleEndian)
                                Array.Reverse(charBytes);
                            return charBytes;
                        })
                        .Where((_, index) => index % 2 == 0)
                        .ToArray());
            }
            catch
            {
            }

            return (false, null);
        }

        public static Stream ToStream(this string str, Encoding encoding = null)
        {
            if (encoding == null)
                encoding = Encoding.UTF8;

            return new MemoryStream(encoding.GetBytes(str));
        }

        /// <summary>
        /// Adds an element to a double string array.
        /// <para>Ajoute un élément à une liste double de strings.</para>
        /// </summary>
        /// <param name="original">The original double array.</param>
        /// <param name="bytesToRead">The new array to add.</param>
        /// <returns>A double array of strings.</returns>
        public static string[][] AddArray(this string[][] original, string[] newElement)
        {
            int newSize = original.Length + 1;
            string[][] newArray = new string[newSize][];
            Parallel.For(0, original.Length, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, (i, stateInner) =>
            {
                newArray[i] = original[i];
            });
            newArray[newSize - 1] = newElement;
            return newArray;
        }

        public unsafe static List<string> ParseJsonStringProperty(this string jsonText, string property)
        {
            List<string> result = new List<string>();

            if (string.IsNullOrEmpty(jsonText))
                return result;

            // Uses SIMD Json when possible.
#if NETCOREAPP3_0_OR_GREATER
            if (Avx2.IsSupported)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(jsonText);

                if (Windows.Win32API.IsWindows)
                {
                    fixed (byte* ptr = bytes) // pin bytes while we are working on them
                        using (ParsedJsonN doc = SimdJsonN.ParseJson(ptr, bytes.Length))
                        {
                            if (!doc.IsValid)
                                return result;

                            // Open iterator
                            using (ParsedJsonIteratorN iterator = doc.CreateIterator())
                            {
                                while (iterator.MoveForward())
                                {
                                    if (iterator.IsString && iterator.GetUtf16String() == property)
                                    {
                                        if (iterator.MoveForward())
                                            result.Add(iterator.GetUtf16String());
                                    }
                                }
                            }

                            return result;
                        }
                }
                else
                {
                    fixed (byte* ptr = bytes) // pin bytes while we are working on them
                        using (ParsedJson doc = SimdJson.ParseJson(ptr, bytes.Length))
                        {
                            if (!doc.IsValid)
                                return result;

                            // Open iterator
                            using (ParsedJsonIterator iterator = doc.CreateIterator())
                            {
                                while (iterator.MoveForward())
                                {
                                    if (iterator.IsString && iterator.GetUtf16String() == property)
                                    {
                                        if (iterator.MoveForward())
                                            result.Add(iterator.GetUtf16String());
                                    }
                                }
                            }

                            return result;
                        }
                }
            }
#else
            if (Windows.Win32API.IsWindows && IsProcessorFeaturePresent(PF_AVX2_INSTRUCTIONS_AVAILABLE))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(jsonText);
                fixed (byte* ptr = bytes) // pin bytes while we are working on them
                    using (ParsedJsonN doc = SimdJsonN.ParseJson(ptr, bytes.Length))
                    {
                        if (!doc.IsValid)
                            return result;

                        // Open iterator
                        using (ParsedJsonIteratorN iterator = doc.CreateIterator())
                        {
                            while (iterator.MoveForward())
                            {
                                if (iterator.IsString && iterator.GetUtf16String() == property)
                                {
                                    if (iterator.MoveForward())
                                        result.Add(iterator.GetUtf16String());
                                }
                            }
                        }

                        return result;
                    }
            }
#endif
            else
            {
                try
                {
                    using (var doc = JsonDocument.Parse(jsonText))
                        FindPropertyValuesNested(doc.RootElement, result, property);
                }
                catch 
                {
                }

                return result;
            }
        }

        // Recursive method to find all the requested property values in any nested structure
        private static void FindPropertyValuesNested(JsonElement element, List<string> output, string property)
        {
            // If the element is an object, traverse through its properties
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty nestedProperty in element.EnumerateObject())
                {
                    // If the property name matches the requested property, add it to the output list
                    if (nestedProperty.Name == property)
                        output.Add(nestedProperty.Value.ToString());

                    // Recurse on the value of this property
                    FindPropertyValuesNested(nestedProperty.Value, output, property);
                }
            }
            // If the element is an array, process each item in the array
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement nestedArrayItem in element.EnumerateArray())
                {
                    FindPropertyValuesNested(nestedArrayItem, output, property);
                }
            }
        }
    }
}
