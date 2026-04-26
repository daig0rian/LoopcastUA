using System;
using System.Security.Cryptography;
using System.Text;

namespace LoopcastUA.Config
{
    internal static class DpapiProtector
    {
        private const string Prefix = "DPAPI:";

        public static string Protect(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return plaintext;
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(encrypted);
        }

        public static string Unprotect(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
            var encrypted = Convert.FromBase64String(value.Substring(Prefix.Length));
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }

        public static bool IsProtected(string value)
        {
            return value != null && value.StartsWith(Prefix, StringComparison.Ordinal);
        }
    }
}
