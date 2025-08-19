using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace GameNetServer.Data
{
    public static class Database
    {
        private static string _connString;

        public static async Task InitializeAsync(string dbFilePath)
        {
            var dir = Path.GetDirectoryName(dbFilePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = dbFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            };
            _connString = csb.ToString();

            using (var conn = new SqliteConnection(_connString))
            {
                await conn.OpenAsync().ConfigureAwait(false);

                // Set pragmas for concurrency + safety
                using (var pragma = conn.CreateCommand())
                {
                    pragma.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;";
                    await pragma.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS users (
  id               INTEGER PRIMARY KEY AUTOINCREMENT,
  username         TEXT NOT NULL UNIQUE,
  pass_hash        TEXT NOT NULL,
  created_utc      TEXT NOT NULL,
  last_login_utc   TEXT NULL,
  failed_count     INTEGER NOT NULL DEFAULT 0,
  is_locked        INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS sessions (
  id            TEXT PRIMARY KEY,          -- random token
  user_id       INTEGER NOT NULL,
  created_utc   TEXT NOT NULL,
  expires_utc   TEXT NOT NULL,
  FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE
);
";
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"CREATE UNIQUE INDEX IF NOT EXISTS idx_users_username_nocase
                        ON users (lower(username));";
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        public static SqliteConnection OpenConnection() => new SqliteConnection(_connString);


    }
}
