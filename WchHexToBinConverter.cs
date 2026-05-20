/*
 * WchHexToBinConverter.cs - WCH HEX to BIN Converter
 * 
 * This file provides Intel HEX to binary conversion functionality
 * specifically designed for WCH MCU IAP programming.
 * 
 * WCH-specific operations:
 * - Supports Extended Linear Address records (type 0x04) for addresses > 64KB
 * - Handles Start Segment Address records (type 0x03) - ignored for RISC-V MCUs
 * - Returns actual start address from HEX file for proper flash programming
 * - Fills gaps with 0xFF (erased flash state)
 * 
 * Supported Intel HEX record types:
 * - 0x00: Data record
 * - 0x01: End of file record
 * - 0x03: Start segment address record (ignored)
 * - 0x04: Extended linear address record
 * - 0x05: Start linear address record (ignored)
 * 
 * Usage:
 *   var result = WchHexToBinConverter.ConvertHexToBin("firmware.hex");
 *   byte[] binData = result.Data;
 *   uint startAddress = result.StartAddress;
 * 
 * Author: WCH IAP Tool
 * Date: 2025
 */

using System;
using System.Collections.Generic;
using System.IO;

namespace WchHexConverter
{
    /// <summary>
    /// WCH HEX to BIN converter for MCU IAP programming
    /// </summary>
    public class WchHexToBinConverter
    {
        /// <summary>
        /// Result of HEX to BIN conversion
        /// </summary>
        public class HexFileResult
        {
            /// <summary>
            /// Converted binary data
            /// </summary>
            public byte[] Data { get; set; }

            /// <summary>
            /// Start address from HEX file (lowest address of data records)
            /// </summary>
            public uint StartAddress { get; set; }
        }

        /// <summary>
        /// Convert Intel HEX file to binary format
        /// </summary>
        /// <param name="hexFilePath">Path to the HEX file</param>
        /// <returns>HexFileResult containing binary data and start address</returns>
        public static HexFileResult ConvertHexToBin(string hexFilePath)
        {
            var lines = File.ReadAllLines(hexFilePath);
            var dataSegments = new List<(uint address, byte[] data)>();
            uint baseAddress = 0;
            uint minAddress = uint.MaxValue;
            uint maxAddress = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] != ':')
                    continue;

                var data = ParseHexLine(line);
                if (data == null)
                    continue;

                byte recordType = data[3];

                switch (recordType)
                {
                    case 0x00: // Data record
                        uint address = baseAddress + (uint)((data[1] << 8) | data[2]);
                        int dataLen = data[0];
                        byte[] segmentData = new byte[dataLen];
                        Array.Copy(data, 4, segmentData, 0, dataLen);
                        dataSegments.Add((address, segmentData));

                        if (address < minAddress)
                            minAddress = address;
                        if (address + (uint)dataLen > maxAddress)
                            maxAddress = address + (uint)dataLen;
                        break;

                    case 0x02: // Extended segment address record
                        baseAddress = (uint)((data[4] << 8) | data[5]) << 4;
                        break;

                    case 0x04: // Extended linear address record
                        // WCH: Used for addresses above 64KB (e.g., 0x08010000)
                        baseAddress = (uint)((data[4] << 8) | data[5]) << 16;
                        break;

                    case 0x03: // Start segment address record
                        // WCH: Ignored for RISC-V MCUs (80x86 specific)
                        break;

                    case 0x05: // Start linear address record
                        // WCH: Ignored - not needed for flash programming
                        break;

                    case 0x01: // End of file record
                        goto EndOfFile;
                }
            }

        EndOfFile:
            if (dataSegments.Count == 0)
                return new HexFileResult { Data = new byte[0], StartAddress = 0 };

            // Create continuous buffer from min to max address
            uint totalSize = maxAddress - minAddress;
            byte[] binData = new byte[totalSize];

            // WCH: Fill with 0xFF (erased flash state)
            for (int i = 0; i < binData.Length; i++)
                binData[i] = 0xFF;

            // Copy segments to buffer
            foreach (var segment in dataSegments)
            {
                uint offset = segment.address - minAddress;
                Array.Copy(segment.data, 0, binData, offset, segment.data.Length);
            }

            return new HexFileResult { Data = binData, StartAddress = minAddress };
        }

        /// <summary>
        /// Parse a single Intel HEX line
        /// </summary>
        /// <param name="line">HEX line starting with ':'</param>
        /// <returns>Parsed data array or null if invalid</returns>
        public static byte[] ParseHexLine(string line)
        {
            try
            {
                if (string.IsNullOrEmpty(line) || line[0] != ':')
                    return null;

                int byteCount = System.Convert.ToInt32(line.Substring(1, 2), 16);
                byte[] data = new byte[byteCount + 5];

                data[0] = (byte)byteCount;
                data[1] = System.Convert.ToByte(line.Substring(3, 2), 16);
                data[2] = System.Convert.ToByte(line.Substring(5, 2), 16);
                data[3] = System.Convert.ToByte(line.Substring(7, 2), 16);

                for (int i = 0; i < byteCount; i++)
                {
                    data[4 + i] = System.Convert.ToByte(line.Substring(9 + i * 2, 2), 16);
                }

                byte checksum = System.Convert.ToByte(line.Substring(9 + byteCount * 2, 2), 16);
                data[data.Length - 1] = checksum;

                if (!VerifyChecksum(data))
                {
                    throw new InvalidDataException($"Checksum error in line: {line}");
                }

                return data;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Failed to parse hex line: {line}", ex);
            }
        }

        /// <summary>
        /// Verify checksum of a parsed HEX line
        /// </summary>
        /// <param name="data">Parsed data including checksum as last byte</param>
        /// <returns>True if checksum is valid</returns>
        private static bool VerifyChecksum(byte[] data)
        {
            int sum = 0;
            for (int i = 0; i < data.Length - 1; i++)
            {
                sum += data[i];
            }
            sum = (~sum + 1) & 0xFF;
            return sum == data[data.Length - 1];
        }
    }
}
