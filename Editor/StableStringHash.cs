namespace UnityEditor.U2D.Common
{
    // Matches string.GetHashCode() in the Mono Editor, including stopping at an embedded '\0'.
    // Unlike string.GetHashCode() on CoreCLR, it returns the same value in every process.
    internal static class StableStringHash
    {
        public static int Compute(string str)
        {
            int hash1 = 5381;
            int hash2 = hash1;
            int length = str.Length;

            for (int i = 0; i < length; i += 2)
            {
                int c = str[i];
                if (c == 0)
                    break;
                hash1 = ((hash1 << 5) + hash1) ^ c;

                if (i + 1 == length)
                    break;
                c = str[i + 1];
                if (c == 0)
                    break;
                hash2 = ((hash2 << 5) + hash2) ^ c;
            }

            return hash1 + (hash2 * 1566083941);
        }
    }
}
