using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;

namespace GameNet.Data
{
    public static class Passwords
    {
        // Returns an encoded Argon2 string that includes parameters+salt.
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
