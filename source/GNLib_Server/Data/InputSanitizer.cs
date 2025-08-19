using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GameNetServer.Data
{
    public static class InputSanitizer
    {
        /// <summary>
        /// Canonicalizes and cleans a username for validation/storage.
        /// - Trims Unicode whitespace (both ends)
        /// - NFKC normalizes (folds compatibility chars)
        /// - Removes control chars
        /// - Removes all internal whitespace
        /// - Optionally folds to ASCII (removes diacritics / non-ASCII)
        /// - Collapses repeated separators (., _, -) and strips them at ends
        /// NOTE: This does NOT guarantee validity; run UsernameValidator afterwards.
        /// </summary>
        public static string SanitizeUsername(string input, bool foldToAscii = true)
        {
            if (input == null) return string.Empty;

            string s = TrimUnicodeWhitespace(input);
            s = s.Normalize(NormalizationForm.FormKC);
            s = RemoveControlChars(s);
            s = RemoveUnicodeWhitespace(s);

            if (foldToAscii)
                s = AsciiFold(s);

            // Collapse multiple separators and strip from ends
            s = Regex.Replace(s, @"[._-]{2,}", m => m.Value.Substring(0, 1));
            s = Regex.Replace(s, @"^[._-]+|[._-]+$", "");

            return s;
        }

        /// <summary>
        /// Canonicalizes a password WITHOUT changing its visible content.
        /// - Optionally NFC normalizes (disabled by default to avoid surprises)
        /// - Removes NUL characters which can break interop
        /// DO NOT trim or case-fold passwords here. Validate with PasswordValidator.
        /// </summary>
        public static string SanitizePassword(string input, bool normalize = false)
        {
            if (input == null) return string.Empty;

            string s = input;
            if (normalize)
                s = s.Normalize(NormalizationForm.FormC);

            // Strip embedded NULs; everything else is left intact for hashing
            if (s.IndexOf('\0') >= 0)
            {
                var sb = new StringBuilder(s.Length);
                for (int i = 0; i < s.Length; i++)
                    if (s[i] != '\0') sb.Append(s[i]);
                s = sb.ToString();
            }

            return s;
        }

        // ---- helpers ----

        private static string TrimUnicodeWhitespace(string s)
        {
            int start = 0, end = s.Length - 1;
            while (start <= end && char.IsWhiteSpace(s[start])) start++;
            while (end >= start && char.IsWhiteSpace(s[end])) end--;
            return (start == 0 && end == s.Length - 1) ? s : s.Substring(start, end - start + 1);
        }

        private static string RemoveUnicodeWhitespace(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
                if (!char.IsWhiteSpace(s[i])) sb.Append(s[i]);
            return sb.ToString();
        }

        private static string RemoveControlChars(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
                if (!char.IsControl(s[i])) sb.Append(s[i]);
            return sb.ToString();
        }

        /// <summary>
        /// Best-effort ASCII folding: removes diacritics and drops non-ASCII.
        /// </summary>
        private static string AsciiFold(string s)
        {
            // Decompose (FormD) to split base chars and diacritics
            string d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            for (int i = 0; i < d.Length; i++)
            {
                char c = d[i];
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc == UnicodeCategory.NonSpacingMark) continue; // drop diacritics

                if (c <= 0x7F) sb.Append(c); // keep ASCII only
                // else: drop non-ASCII (you can map specific chars if desired)
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
