using Isopoh.Cryptography.Argon2;

namespace GameNetServer.Data
{
    public static class HashTool
    {
        public static string Hash(string password)
        {
            // Library encodes params/salt into the returned string.
            return Argon2.Hash(password);
        }
        public static bool Verify(string password, string encodedHash)
        {
            return Argon2.Verify(encodedHash, password);
        }
    }

}
