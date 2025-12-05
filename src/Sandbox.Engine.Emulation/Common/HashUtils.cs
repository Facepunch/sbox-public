using System;
using System.Text;

namespace Sandbox.Engine.Emulation.Common;

/// <summary>
/// Utilitaires de hash pour l'émulation.
/// Utilise MurmurHash2 pour garantir la compatibilité avec Source 2.
/// </summary>
public static unsafe class HashUtils
{
    /// <summary>
    /// Hash a string using MurmurHash2 algorithm (same as Source 2).
    /// Uses lowercase = true and seed = 0x31415926 to match StringToken behavior.
    /// 
    /// **Comportement Source 2** : Utilise MurmurHash2 avec seed = 0x31415926 (STRINGTOKEN_MURMURHASH_SEED).
    /// **Comportement émulation** : Identique, garantit la compatibilité des hash.
    /// 
    /// **Référence** : 
    /// - `engine/Sandbox.System/Utility/StringToken.cs` ligne 36 : `value.MurmurHash2( true )`
    /// - `engine/Sandbox.System/Extend/StringExtensions.cs` ligne 716 : Implémentation Source 2
    /// </summary>
    /// <param name="str">String to hash</param>
    /// <param name="lowercase">If true, convert string to lowercase before hashing (default: false)</param>
    /// <param name="seed">Seed value for the hash (default: 0x31415926, matches Source 2)</param>
    /// <returns>Hash value as uint</returns>
    public static uint MurmurHash2(string str, bool lowercase = false, uint seed = 0x31415926)
    {
        if (string.IsNullOrEmpty(str))
            return 0;

        if (lowercase)
            str = str.ToLowerInvariant();

        byte[] bytes = Encoding.ASCII.GetBytes(str);
        uint len = (uint)bytes.Length;
        const uint m = 0x5bd1e995;
        const int r = 24;

        uint h = seed ^ len;

        fixed (byte* data = bytes)
        {
            uint* data32 = (uint*)data;
            while (len >= 4)
            {
                uint k = *data32;

                k *= m;
                k ^= k >> r;
                k *= m;

                h *= m;
                h ^= k;

                data32++;
                len -= 4;
            }

            byte* dataRemaining = (byte*)data32;
            switch (len)
            {
                case 3: h ^= (uint)dataRemaining[2] << 16; goto case 2;
                case 2: h ^= (uint)dataRemaining[1] << 8; goto case 1;
                case 1:
                    h ^= dataRemaining[0];
                    h *= m;
                    break;
            }

            h ^= h >> 13;
            h *= m;
            h ^= h >> 15;
        }

        return h;
    }
}

