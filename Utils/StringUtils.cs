﻿using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LanPlayServer.Utils
{
    static class StringUtils
    {
        public static byte[] GetFixedLengthBytes(string inputString, int size, Encoding encoding)
        {
            inputString += "\0";

            int bytesCount = encoding.GetByteCount(inputString);

            byte[] output = new byte[size];

            if (bytesCount < size)
            {
                encoding.GetBytes(inputString, 0, inputString.Length, output, 0);
            }
            else
            {
                int nullSize = encoding.GetByteCount("\0");

                output = encoding.GetBytes(inputString);

                Array.Resize(ref output, size - nullSize);

                output = output.Concat(encoding.GetBytes("\0")).ToArray();
            }

            return output;
        }

        public static byte[] HexToBytes(string hexString)
        {
            // Ignore last character if HexLength % 2 != 0.
            int bytesInHex = hexString.Length / 2;

            byte[] output = new byte[bytesInHex];

            for (int index = 0; index < bytesInHex; index++)
            {
                output[index] = byte.Parse(hexString.Substring(index * 2, 2), NumberStyles.HexNumber);
            }

            return output;
        }

        public static string ReadUtf8String(Span<byte> data, int index = 0)
        {
            int size = data.Length;

            using MemoryStream ms = new();
            while (size-- > 0)
            {
                byte value = data[index++];

                if (value == 0)
                {
                    break;
                }

                ms.WriteByte(value);
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        public static string CleanInput(this string input, int maxLength = -1, string extraAllowedChars = "")
        {
            if (input == null)
            {
                return null;
            }

            string result = input;

            if (maxLength != -1 && input.Length > maxLength)
            {
                result = result[..maxLength];
            }

            try {
                return Regex.Replace(result, $@"[^\w-_{extraAllowedChars}]", "",
                    RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
            }
            catch (RegexMatchTimeoutException) {
                return string.Empty;
            }
        }
    }
}
