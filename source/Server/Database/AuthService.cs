using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GameNet.Data
{
    public sealed class AuthService
    {
        private readonly UserRepository _users = new UserRepository();
        private readonly SessionRepository _sessions = new SessionRepository();

        public static string CreateToken(int numBytes = 32)
        {
            var bytes = new byte[numBytes];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);

            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2")); // lower-case hex

            return sb.ToString(); // 64 hex chars for 32 bytes
        }

        public async Task<(bool ok, string message, string sessionToken)> LoginAsync(string username, string password, TimeSpan sessionTtl)
        {
            var rec = await _users.FindByUsernameAsync(username).ConfigureAwait(false);
            if (rec == null) return (false, "Invalid credentials.", null);

            var userId = rec.Value.userId;
            var passHash = rec.Value.passHash;
            if (!Passwords.Verify(password, passHash))
            {
                var fails = await _users.IncrementFailedAsync(userId).ConfigureAwait(false);
                return (false, $"Invalid credentials. Failed attempts: {fails}.", null);
            }

            await _users.RecordLoginSuccessAsync(userId).ConfigureAwait(false);
            var token = await _sessions.CreateSessionAsync(userId, sessionTtl).ConfigureAwait(false);
            return (true, "OK", token);
        }

        public async Task<(bool ok, string message)> RegisterAsync(string username, string password)
        {
            // 1) Validate username
            var (uok, uerrs) = UsernameValidator.Validate(username);
            // 2) Validate password (may depend on username)
            var (pok, perrs) = PasswordValidator.Validate(password, username);

            if (!uok || !pok)
            {
                var all = uerrs.Concat(perrs).ToArray();
                var msg = all.Length == 1 ? all[0] : string.Join("; ", all);
                return (false, msg);
            }

            try
            {
                await _users.CreateUserAsync(username, password).ConfigureAwait(false);
                return (true, "Created");
            }
            catch (Exception ex)
            {
                // Handle case-insensitive duplicates if the DB index is present (see step 4)
                if (ex.Message.IndexOf("UNIQUE", StringComparison.OrdinalIgnoreCase) >= 0)
                    return (false, "Username already exists.");
                throw;
            }
        }
    }
}
