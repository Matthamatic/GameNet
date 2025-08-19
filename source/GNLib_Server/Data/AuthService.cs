using GameNet.Common;
using GameNet.Data;
using GameNetServer.Data;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GameNetServer.Data
{
    public static class AuthService
    {
        private const int SESSIONLENGTH = 24; // Hours a session stays valid   

        private static readonly UserRepository _users = new UserRepository();
        private static readonly SessionRepository _sessions = new SessionRepository();

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

        public static async Task<LoginResult> LoginAsync(string username, string password)
        {
            username = InputSanitizer.SanitizeUsername(username);
            password = InputSanitizer.SanitizePassword(password);

            // Get the user data from the db
            var rec = await _users.FindByUsernameAsync(username).ConfigureAwait(false);
            // If the result is null the user does not exist 
            if (rec == null) return new LoginResult(false, "Invalid credentials.", "");

            var userId = rec.Value.userId;

            // Check the password is correct (against the hash)
            if (!HashTool.Verify(password, rec.Value.passHash))
            {
                var fails = await _users.IncrementFailedAsync(userId).ConfigureAwait(false);
                return new LoginResult(false, $"Invalid credentials. Failed attempts: {fails}.", "");
            }

            // Create and record the session
            await _users.RecordLoginSuccessAsync(userId).ConfigureAwait(false);
            var token = await _sessions.CreateSessionAsync(userId, TimeSpan.FromHours(SESSIONLENGTH)).ConfigureAwait(false);
            return new LoginResult(true, "OK", token);
        }

        public static async Task<RegisterResult> RegisterAsync(string username, string password, string optionalEmail = "", string optionalInfo = "")
        {
            username = InputSanitizer.SanitizeUsername(username);
            password = InputSanitizer.SanitizePassword(password);

            // Validate username
            var (uok, uerrs) = UsernameValidator.Validate(username);
            // Validate password (may depend on username)
            var (pok, perrs) = PasswordValidator.Validate(password, username);

            // If the username and/or password are not valid, send back the details
            if (!uok || !pok)
            {
                var all = uerrs.Concat(perrs).ToArray();
                var msg = all.Length == 1 ? all[0] : string.Join("; ", all);
                return new RegisterResult(false, msg);
            }

            try
            {
                await _users.CreateUserAsync(username, password).ConfigureAwait(false);
                Console.WriteLine($"[Registered] user='{username}', email='{optionalEmail}', info='{optionalInfo}'");
                return new RegisterResult(true, "Created");
            }
            catch (Exception ex)
            {
                // Handle case-insensitive duplicates if the DB index is present (see step 4)
                if (ex.Message.IndexOf("UNIQUE", StringComparison.OrdinalIgnoreCase) >= 0)
                { return new RegisterResult(false, "Username already exists."); }
                else
                {
                    Console.WriteLine($"ERROR while registering user!\n" +
                        $"User Data: name='{username}', email='{optionalEmail}', info='{optionalInfo}'\n" +
                        $"{ex}");
                    return new RegisterResult(false, "Error creating account. Contact an administaitor.");
                }
                //throw;
            }
        }

        public struct LoginResult
        {
            public LoginResult(bool accepted, string message, string token)
            {
                Accepted = accepted;
                Message = message;
                Token = token;
            }
            public bool Accepted { get; private set; }
            public string Message {get; private set; }
            public string Token { get; private set; }
        }
        public struct RegisterResult
        {
            public RegisterResult(bool accepted, string message)
            {
                Accepted = accepted;
                Message = message;
            }
            public bool Accepted { get; private set; }
            public string Message { get; private set; }
        }
    }

    


}
