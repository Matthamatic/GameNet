using System;
using System.Threading.Tasks;
using GameNet.Common;
using Microsoft.Data.Sqlite;

namespace GameNetServer.Data
{
    public sealed class UserRepository
    {
        public async Task<int> CreateUserAsync(string username, string password)
        {
            var hash = HashTool.Hash(password);
            using (var conn = Database.OpenConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
INSERT INTO users (username, pass_hash, created_utc)
VALUES ($u, $h, $c);
SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("$u", username);
                    cmd.Parameters.AddWithValue("$h", hash);
                    cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));
                    var id = (long)await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                    return (int)id;
                }
            }
        }

        public async Task<(int userId, string passHash)?> FindByUsernameAsync(string username)
        {
            using (var conn = Database.OpenConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT id, pass_hash FROM users WHERE username = $u;";
                    cmd.Parameters.AddWithValue("$u", username);
                    using (var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        if (!await r.ReadAsync().ConfigureAwait(false)) return null;
                        return (r.GetInt32(0), r.GetString(1));
                    }
                }
            }
        }

        public async Task RecordLoginSuccessAsync(int userId)
        {
            using (var conn = Database.OpenConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
UPDATE users
SET last_login_utc = $t, failed_count = 0
WHERE id = $id;";
                    cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("$id", userId);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        public async Task<int> IncrementFailedAsync(int userId)
        {
            using (var conn = Database.OpenConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
UPDATE users
SET failed_count = failed_count + 1
WHERE id = $id;
SELECT failed_count FROM users WHERE id = $id;";
                    cmd.Parameters.AddWithValue("$id", userId);
                    var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                    return Convert.ToInt32(result);
                }
            }
        }
    }
}
