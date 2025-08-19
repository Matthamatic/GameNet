using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GameNet.Data
{
    public static class PasswordValidator
    {
        // Policy (tune as you like)
        public const int MinLength = 12;
        public const int MaxLength = 128;
        public const int MinCategories = 3; // require at least 3 of the 4 classes

        private static readonly Regex HasLower = new Regex("[a-z]", RegexOptions.Compiled);
        private static readonly Regex HasUpper = new Regex("[A-Z]", RegexOptions.Compiled);
        private static readonly Regex HasDigit = new Regex(@"\d", RegexOptions.Compiled);
        private static readonly Regex HasSymbol = new Regex(@"[^A-Za-z0-9]", RegexOptions.Compiled);
        private static readonly Regex TripleRepeat = new Regex(@"(.)\1{2,}", RegexOptions.Compiled);
        private static readonly Regex HasWhitespace = new Regex(@"\s", RegexOptions.Compiled);

        // Small blacklist; expand or load from a file if desired.
        private static readonly HashSet<string> Common =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "password","123456","12345678","qwerty","letmein","111111","abc123","iloveyou",
                "admin","welcome","monkey","dragon","trustno1","123456789","1234567","baseball",
                "qwertyuiop","qazwsx","passw0rd","zaq12wsx"
            };

        public static (bool ok, string[] errors) Validate(string password, string username)
        {
            var errs = new List<string>();

            if (string.IsNullOrEmpty(password))
                errs.Add("Password is required.");
            else
            {
                if (password.Length < MinLength || password.Length > MaxLength)
                    errs.Add($"Password must be {MinLength}-{MaxLength} characters.");

                if (HasWhitespace.IsMatch(password))
                    errs.Add("Password cannot contain whitespace.");

                int categories =
                    (HasLower.IsMatch(password) ? 1 : 0) +
                    (HasUpper.IsMatch(password) ? 1 : 0) +
                    (HasDigit.IsMatch(password) ? 1 : 0) +
                    (HasSymbol.IsMatch(password) ? 1 : 0);

                if (categories < MinCategories)
                    errs.Add("Use at least three of: lowercase, uppercase, digit, symbol.");

                if (TripleRepeat.IsMatch(password))
                    errs.Add("Avoid 3+ identical characters in a row.");

                if (!string.IsNullOrWhiteSpace(username))
                {
                    var u = username.Trim();
                    if (u.Length >= 3)
                    {
                        if (password.IndexOf(u, StringComparison.OrdinalIgnoreCase) >= 0)
                            errs.Add("Password must not contain your username.");
                        var ur = Reverse(u);
                        if (password.IndexOf(ur, StringComparison.OrdinalIgnoreCase) >= 0)
                            errs.Add("Password must not contain your username reversed.");
                    }
                }

                if (Common.Contains(password))
                    errs.Add("Password is too common.");

                if (HasSimpleSequence(password))
                    errs.Add("Avoid simple sequences like 'abcd' or '1234'.");
            }

            return (errs.Count == 0, errs.ToArray());
        }

        private static string Reverse(string s)
        {
            var arr = s.ToCharArray();
            Array.Reverse(arr);
            return new string(arr);
        }

        // Detects ascending/descending ASCII sequences of length >= 4 across letters or digits.
        private static bool HasSimpleSequence(string s, int minRun = 4)
        {
            if (string.IsNullOrEmpty(s)) return false;

            int upRun = 1, downRun = 1;
            for (int i = 1; i < s.Length; i++)
            {
                int delta = s[i] - s[i - 1];

                if (delta == 1) { upRun++; downRun = 1; }
                else if (delta == -1) { downRun++; upRun = 1; }
                else { upRun = 1; downRun = 1; }

                // Only count if both chars are digits or both letters
                bool sameClass =
                    (char.IsDigit(s[i]) && char.IsDigit(s[i - 1])) ||
                    (char.IsLetter(s[i]) && char.IsLetter(s[i - 1]));

                if (!sameClass) { upRun = 1; downRun = 1; continue; }

                if (upRun >= minRun || downRun >= minRun) return true;
            }
            return false;
        }
    }
}
