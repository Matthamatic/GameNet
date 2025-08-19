using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GameNet.Data
{
    public static class UsernameValidator
    {
        // Policy
        public const int MinLength = 3;
        public const int MaxLength = 32;

        // Start/end with letter or digit; internal chars may be letter/digit . _ -
        // ASCII only for now.
        private static readonly Regex Allowed =
            new Regex(@"^[A-Za-z0-9](?:[A-Za-z0-9._-]{1,30}[A-Za-z0-9])?$", RegexOptions.Compiled);

        private static readonly Regex DoubleSep =
            new Regex(@"[._-]{2,}", RegexOptions.Compiled);

        private static readonly HashSet<string> Reserved =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "admin","root","system","null","undefined","me","you","owner","support" };

        public static (bool ok, string[] errors) Validate(string username)
        {
            var errs = new List<string>();

            if (string.IsNullOrWhiteSpace(username))
                errs.Add("Username is required.");
            else
            {
                if (username.Length < MinLength || username.Length > MaxLength)
                    errs.Add($"Username must be {MinLength}-{MaxLength} characters.");

                // ASCII only (simple rule; relax if you want Unicode usernames)
                for (int i = 0; i < username.Length; i++)
                    if (username[i] > 0x7F) { errs.Add("Username must use ASCII characters only."); break; }

                if (!Allowed.IsMatch(username))
                    errs.Add("Username may contain letters, digits, '.', '_' or '-', must start/end with a letter or digit.");

                if (DoubleSep.IsMatch(username))
                    errs.Add("Username cannot contain repeated separators (e.g., '..', '__', '--').");

                if (Reserved.Contains(username))
                    errs.Add("This username is reserved.");
            }

            return (errs.Count == 0, errs.ToArray());
        }
    }
}
