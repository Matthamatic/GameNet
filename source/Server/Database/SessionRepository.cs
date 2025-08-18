using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace GameNet.Data
{
    public sealed class SessionRepository
    {
        public async Task<string> CreateSessionAsync(int userId, TimeSpan ttl)
        {
            var token = AuthService.CreateToken();
            var now = DateTime.UtcNow;
            using (var conn = Database.OpenConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
INSERT INTO sessions (id, user_id, created_utc, expires_utc)
VALUES ($id, $uid, $c, $e);";
                    cmd.Parameters.AddWithValue("$id", token);
                    cmd.Parameters.AddWithValue("$uid", userId);
                    cmd.Parameters.AddWithValue("$c", now.ToString("o"));
                    cmd.Parameters.AddWithValue("$e", now.Add(ttl).ToString("o"));
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            return token;
        }

        public async Task<int?> ValidateSessionAsync(string token)
        {
            using (var conn = Database.OpenConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT user_id FROM sessions
WHERE id = $id AND expires_utc > $now;";
                    cmd.Parameters.AddWithValue("$id", token);
                    cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                    var r = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                    return r == null ? (int?)null : Convert.ToInt32(r);
                }
            }
        }

        public async Task<int> RevokeSessionsAsync(int userId)
        {
            using (var conn = Database.OpenConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"DELETE FROM sessions WHERE user_id = $u;";
                    cmd.Parameters.AddWithValue("$u", userId);
                    return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
