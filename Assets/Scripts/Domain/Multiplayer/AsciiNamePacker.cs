namespace Pho.Domain.Multiplayer
{
    /// <summary>
    /// Packs a short player name into four <see cref="ulong"/>s and back.
    ///
    /// <b>Why this exists.</b> Replicating a player's chosen name needs a
    /// fixed-size, allocation-free value: a network variable cannot hold a
    /// <c>string</c>. The obvious tool is Unity's <c>FixedString32Bytes</c>,
    /// but reaching it would require adding <c>Unity.Collections</c> to the
    /// networking assembly's references -- an assembly-graph change that
    /// architecture.md section 10 explicitly routes through integration
    /// rather than letting a feature author make in passing. Four ulongs need
    /// no reference from anybody, and as a bonus the encoding becomes pure
    /// and testable instead of trusted.
    ///
    /// <b>ASCII only, deliberately.</b> 32 bytes of UTF-8 would let a name
    /// truncate mid-codepoint and arrive as a replacement character on the
    /// other three machines. Non-ASCII input is mapped to '?' -- visibly
    /// wrong, which is the honest outcome, rather than subtly corrupt. Phở
    /// itself is spelled in the UI's own strings, not in player names.
    /// </summary>
    public static class AsciiNamePacker
    {
        /// <summary>Bytes available across the four blocks. Names longer than this are truncated by <see cref="Pack"/>.</summary>
        public const int MaxLength = 32;

        const byte Substitute = (byte)'?';

        public static void Pack(string value, out ulong a, out ulong b, out ulong c, out ulong d, out int length)
        {
            a = b = c = d = 0UL;
            length = 0;
            if (string.IsNullOrEmpty(value)) return;

            length = value.Length < MaxLength ? value.Length : MaxLength;

            for (int i = 0; i < length; i++)
            {
                ulong ch = Encode(value[i]);
                int shift = (i % 8) * 8;

                switch (i / 8)
                {
                    case 0: a |= ch << shift; break;
                    case 1: b |= ch << shift; break;
                    case 2: c |= ch << shift; break;
                    default: d |= ch << shift; break;
                }
            }
        }

        public static string Unpack(ulong a, ulong b, ulong c, ulong d, int length)
        {
            if (length <= 0) return string.Empty;
            if (length > MaxLength) length = MaxLength;

            var chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                ulong block = (i / 8) switch { 0 => a, 1 => b, 2 => c, _ => d };
                int shift = (i % 8) * 8;
                chars[i] = (char)(byte)(block >> shift);
            }

            return new string(chars);
        }

        /// <summary>Printable ASCII passes through; everything else (control characters, accents, emoji) becomes '?'.</summary>
        static ulong Encode(char ch) => ch >= ' ' && ch <= '~' ? (ulong)ch : Substitute;
    }
}
