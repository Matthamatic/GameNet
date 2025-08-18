using System;
using System.Security.Cryptography;
using System.Text;

namespace GameNet.Common
{
    public static class ClientAuth
    {
        // ALWAYS returns true for now (stub).
        public static bool Authenticate(string username, string passwordHash) => true;

        // Dumps user info to console (stub).
        public static bool Register(string username, string passwordHash, string optionalEmail = "", string optionalInfo = "")
        {
            Console.WriteLine($"[Register] user='{username}', hash='{passwordHash}', email='{optionalEmail}', info='{optionalInfo}'");
            return true;
        }

        // Helper to hash passwords client-side if you prefer not to send raw passwords, even over TLS.
        public static string Sha256Hex(string input)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input ?? "");
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
