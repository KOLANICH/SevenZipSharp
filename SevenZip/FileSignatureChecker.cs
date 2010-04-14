﻿/*  This file is part of SevenZipSharp.

    SevenZipSharp is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    SevenZipSharp is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with SevenZipSharp.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.IO;

namespace SevenZip
{
#if UNMANAGED
    /// <summary>
    /// The signature checker class. Original code by Siddharth Uppal, adapted by Markhor.
    /// </summary>
    /// <remarks>Based on the code at http://blog.somecreativity.com/2008/04/08/how-to-check-if-a-file-is-compressed-in-c/#</remarks>
    internal static class FileChecker
    {
        private const int SIGNATURE_SIZE = 16;
        private const int SFX_SCAN_LENGTH = 256 * 1024;

        private static bool SpecialDetect(Stream stream, int offset, InArchiveFormat expectedFormat)
        {
            if (stream.Length > offset + SIGNATURE_SIZE)
            {
                var signature = new byte[SIGNATURE_SIZE];
                int bytesRequired = SIGNATURE_SIZE;
                int index = 0;
                stream.Seek(offset, SeekOrigin.Begin);
                while (bytesRequired > 0)
                {
                    int bytesRead = stream.Read(signature, index, bytesRequired);
                    bytesRequired -= bytesRead;
                    index += bytesRead;
                }
                string actualSignature = BitConverter.ToString(signature);
                foreach (string expectedSignature in Formats.InSignatureFormats.Keys)
                {
                    if (Formats.InSignatureFormats[expectedSignature] != expectedFormat)
                    {
                        continue;
                    }
                    if (actualSignature.StartsWith(expectedSignature, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the InArchiveFormat for a specific extension.
        /// </summary>
        /// <param name="stream">The stream to identify.</param>
        /// <param name="offset">The archive beginning offset.</param>
        /// <returns>Corresponding InArchiveFormat.</returns>
        public static InArchiveFormat CheckSignature(Stream stream, out int offset)
        {
            offset = 0;
            if (!stream.CanRead)
            {
                throw new ArgumentException("The stream must be readable.");
            }
            if (stream.Length < SIGNATURE_SIZE)
            {
                throw new ArgumentException("The stream is invalid.");
            }

            #region Get file signature

            var signature = new byte[SIGNATURE_SIZE];
            int bytesRequired = SIGNATURE_SIZE;
            int index = 0;
            stream.Seek(0, SeekOrigin.Begin);
            while (bytesRequired > 0)
            {
                int bytesRead = stream.Read(signature, index, bytesRequired);
                bytesRequired -= bytesRead;
                index += bytesRead;
            }
            string actualSignature = BitConverter.ToString(signature);

            #endregion

            InArchiveFormat suspectedFormat = InArchiveFormat.XZ; // any except PE

            foreach (string expectedSignature in Formats.InSignatureFormats.Keys)
            {
                if (actualSignature.StartsWith(expectedSignature, StringComparison.OrdinalIgnoreCase) ||
                    actualSignature.Substring(6).StartsWith(expectedSignature, StringComparison.OrdinalIgnoreCase) &&
                    Formats.InSignatureFormats[expectedSignature] == InArchiveFormat.Lzh)
                {
                    if (Formats.InSignatureFormats[expectedSignature] == InArchiveFormat.PE)
                    {
                        suspectedFormat = InArchiveFormat.PE;
                    }
                    else
                    {
                        return Formats.InSignatureFormats[expectedSignature];
                    }
                }
            }

            #region SpecialDetect
            try
            {
                SpecialDetect(stream, 257, InArchiveFormat.Tar);
            }
            catch (ArgumentException) {}            
            if (SpecialDetect(stream, 0x8001, InArchiveFormat.Iso))
            {
                return InArchiveFormat.Iso;
            }
            if (SpecialDetect(stream, 0x8801, InArchiveFormat.Iso))
            {
                return InArchiveFormat.Iso;
            }
            if (SpecialDetect(stream, 0x9001, InArchiveFormat.Iso))
            {
                return InArchiveFormat.Iso;
            }
            if (SpecialDetect(stream, 0x9001, InArchiveFormat.Iso))
            {
                return InArchiveFormat.Iso;
            }
            if (SpecialDetect(stream, 0x400, InArchiveFormat.Hfs))
            {
                return InArchiveFormat.Hfs;
            }
            #region Last resort for tar - can mistake
            if (stream.Length >= 1024)
            {
                stream.Seek(-1024, SeekOrigin.End);
                byte[] buf = new byte[1024];
                stream.Read(buf, 0, 1024);
                bool istar = true;
                for (int i = 0; i < 1024; i++)
                {
                    istar = istar && buf[i] == 0;
                }
                if (istar)
                {
                    return InArchiveFormat.Tar;
                }
            }
            #endregion
            #endregion

            #region If a Windows executable, check if it is an SFX archive.
            if (suspectedFormat == InArchiveFormat.PE)
            {
                #region Get first Min(stream.Length, SFX_SCAN_LENGTH) bytes
                var scanLength = Math.Min(stream.Length, SFX_SCAN_LENGTH);
                signature = new byte[scanLength];
                bytesRequired = (int)scanLength;
                index = 0;
                stream.Seek(0, SeekOrigin.Begin);
                while (bytesRequired > 0)
                {
                    int bytesRead = stream.Read(signature, index, bytesRequired);
                    bytesRequired -= bytesRead;
                    index += bytesRead;
                }
                actualSignature = BitConverter.ToString(signature);
                #endregion
                
                foreach (var format in new InArchiveFormat[] 
                {
                    InArchiveFormat.Zip, 
                    InArchiveFormat.SevenZip,
                    InArchiveFormat.Rar,
                    InArchiveFormat.Cab,
                    InArchiveFormat.Arj
                })
                {
                    int pos = actualSignature.IndexOf(Formats.InSignatureFormatsReversed[format]);
                    if (pos > -1)
                    {
                        offset = pos / 3;
                        return format;
                    }
                }
                // No SFX
                return InArchiveFormat.PE;
            }
            #endregion
            throw new ArgumentException("The stream is invalid or no corresponding signature was found.");
        }

        /// <summary>
        /// Gets the InArchiveFormat for a specific file name.
        /// </summary>
        /// <param name="fileName">The archive file name.</param>
        /// <param name="offset">The archive beginning offset.</param>
        /// <returns>Corresponding InArchiveFormat.</returns>
        /// <exception cref="System.ArgumentException"/>
        public static InArchiveFormat CheckSignature(string fileName, out int offset)
        {
            using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                try
                {
                    return CheckSignature(fs, out offset);
                }
                catch (ArgumentException)
                {
                    offset = 0;
                    return Formats.FormatByFileName(fileName, true);
                }
            }
        }
    }
#endif
}