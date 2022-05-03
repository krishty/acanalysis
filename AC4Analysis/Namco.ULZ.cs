using System.Diagnostics;

namespace Namco
{
    class ULZ
    {

        public static byte[] decompress(byte[] input)
        {
            if (input.Length < 20)
                throw new System.IO.EndOfStreamException();
            Debug.Assert(input[0] == 'U' && input[1] == 'l' && input[2] == 'z' && input[3] == 0x1a);

            // • bits  0–23: extracted size
            // • bits 24–31: compression type (“0” or “2”)
            var typeAndSize = readUInt32LE(input, 4);
            var type = typeAndSize >> 24;
            var extractedSize = typeAndSize & 0xFFFFFF;

            // • bits  0–23: offset of symbol table from beginning of archive
            // • bits 24–31: log2 of sliding window
            var offsetBitsAndSymbolsOffset = readUInt32LE(input, 8);
            // The sliding window cannot be larger than 2^15 B (because offset-length pairs are 16-bit integers):
            var offsetBits = (int)(offsetBitsAndSymbolsOffset >> 24);
            if (offsetBits > 15)
                throw new System.IO.IOException("bad ULZ sliding window size");
            var offsetMask = (1U << offsetBits) - 1;

            var i_flags = 16U;
            // Offset of match lookups from beginning of archive.
            var matchesOffset = readUInt32LE(input, 12);
            // There is one stream of new symbols:
            var i_symbol = offsetBitsAndSymbolsOffset & 0xFFFFFF;
            if (i_symbol > input.Length)
                throw new System.IO.IOException("bad ULZ i_symbol");
            // There is one stream of lookups (offset + length) into already-decompressed data:
            var i_matches = matchesOffset;
            if (matchesOffset > input.Length)
                throw new System.IO.IOException("bad ULZ i_matches");
            var i_extracted = 0;

            var extracted = new byte[extractedSize];

            // Both type 0 and type 2 consume bits (starting at the most significant position), but type 1 skips every 32nd bit:
            uint flagMaskIfConsumed;
            switch (type)
            {
                case 0: flagMaskIfConsumed = 1; break;
                case 2: flagMaskIfConsumed = 0; break;
                default:
                    throw new System.IO.IOException("bad ULZ type");
            }

            var bytesLeft = extractedSize;
            if (0 == bytesLeft) // defense (does not happen in real files)
                return extracted;

            var flags = 0U;
            var flagMask = flagMaskIfConsumed; // force refill on first iteration
            do
            {

                if (flagMaskIfConsumed == flagMask) // Need to refill flags?
                {
                    flags = readUInt32LE(input, i_flags);
                    i_flags += 4;
                    flagMask = 0x80000000;
                }

                if (0 != (flags & flagMask))
                {
                    // Copy an uncompressed symbol:
                    extracted[i_extracted++] = input[i_symbol++];
                    --bytesLeft;
                }
                else
                {
                    // Duplicate “length” bytes starting backwards at “offset” in the result. Mind the intentional overlap!
                    var offsetAndLength = readUInt16LE(input, i_matches);
                    i_matches += 2;
                    var offset = offsetAndLength & offsetMask;
                    var length = 3 + (offsetAndLength >> offsetBits);
                    if (length > bytesLeft)
                        throw new System.IO.IOException("bad ULZ offset");

                    for (var i = 0U; i < length; ++i)
                    {
                        extracted[i_extracted] = extracted[i_extracted - 1 - offset];
                        ++i_extracted;
                        --bytesLeft;
                    }
                }

                flagMask >>= 1;
            } while (bytesLeft > 0);

            return extracted;
        }

        private static uint readUInt16LE(byte[] data, uint offset)
        {
            return (uint)data[offset + 0] + ((uint)data[offset + 1] << 8);
        }

        private static uint readUInt32LE(byte[] data, uint offset)
        {
            return (uint)data[offset + 0] + ((uint)data[offset + 1] << 8) + ((uint)data[offset + 2] << 16) + ((uint)data[offset + 3] << 24);
        }

    }
}