using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Settings
{
    /// <summary>A change to the office standard was refused: no PIN, or the wrong one.</summary>
    public sealed class OfficeAuthorizationException : ConfigException
    {
        public OfficeAuthorizationException(string message) : base(message) { }
    }

    /// <summary>
    /// Who may change the office standard (the settings file in the plugin folder).
    ///
    /// Two checks, both required: the office file's folder must be writable from this
    /// computer, and when the office file carries an admin PIN the person must know it.
    /// The PIN is never stored: the office file holds a random salt and the SHA-256 of
    /// salt + PIN, so the file can be read by anyone without giving the PIN away.
    ///
    ///     "admin": { "pinHash": "9f86d0...", "salt": "3a1c..." }
    ///
    /// This is a guard against a well-meaning change to the wrong level, not security
    /// against someone who can edit files in the plugin folder: such a person can remove
    /// the admin section by hand, which is the documented way to reset a forgotten PIN.
    /// Project and personal saves never ask for it.
    /// </summary>
    public static class OfficeAuthorization
    {
        public const string AdminKey = "admin";
        public const string PinHashKey = "pinHash";
        public const string SaltKey = "salt";

        /// <summary>Shortest PIN accepted when one is set.</summary>
        public const int MinimumPinLength = 4;

        /// <summary>16 random bytes, as lower-case hex.</summary>
        public static string NewSalt()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Hex(bytes);
        }

        /// <summary>SHA-256 of the UTF-8 bytes of salt + PIN, as lower-case hex.</summary>
        public static string HashPin(string salt, string pin)
        {
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes((salt ?? string.Empty) + (pin ?? string.Empty))));
        }

        /// <summary>True when <paramref name="pin"/> hashes to <paramref name="pinHash"/>.
        /// Compared in constant time.</summary>
        public static bool Verify(string salt, string pinHash, string pin)
        {
            if (string.IsNullOrWhiteSpace(pinHash) || pin == null) return false;
            var expected = pinHash.Trim().ToLowerInvariant();
            var actual = HashPin(salt, pin);
            if (expected.Length != actual.Length) return false;
            var diff = 0;
            for (var i = 0; i < actual.Length; i++) diff |= expected[i] ^ actual[i];
            return diff == 0;
        }

        /// <summary>A null PIN problem means the PIN can be used.</summary>
        public static string PinProblem(string pin)
        {
            if (string.IsNullOrEmpty(pin)) return "Enter a PIN.";
            if (pin.Length < MinimumPinLength) return "A PIN needs at least " + MinimumPinLength + " characters.";
            if (pin.Trim().Length != pin.Length) return "A PIN cannot start or end with a space.";
            return null;
        }

        /// <summary>The admin section for a new PIN: a fresh salt and the hash, never the PIN.</summary>
        public static JObject CreateAdmin(string pin)
        {
            var problem = PinProblem(pin);
            if (problem != null) throw new ArgumentException(problem, "pin");
            var salt = NewSalt();
            return new JObject
            {
                { PinHashKey, HashPin(salt, pin) },
                { SaltKey, salt }
            };
        }

        /// <summary>True when the admin section holds a PIN hash.</summary>
        public static bool HasPin(JObject admin)
        {
            if (admin == null) return false;
            var hash = admin.GetValue(PinHashKey, StringComparison.OrdinalIgnoreCase);
            return hash != null && hash.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)hash);
        }

        /// <summary>Checks a PIN against an admin section. No PIN set means nothing to check: true.</summary>
        public static bool Verify(JObject admin, string pin)
        {
            if (!HasPin(admin)) return true;
            var hash = (string)admin.GetValue(PinHashKey, StringComparison.OrdinalIgnoreCase);
            var saltToken = admin.GetValue(SaltKey, StringComparison.OrdinalIgnoreCase);
            var salt = saltToken != null && saltToken.Type == JTokenType.String ? (string)saltToken : string.Empty;
            return Verify(salt, hash, pin);
        }

        /// <summary>The admin section of an office settings file, or null when the file or the
        /// section is missing. A file that is not valid JSON is a ConfigException.</summary>
        public static JObject ReadAdmin(string officePath)
        {
            if (string.IsNullOrWhiteSpace(officePath) || !File.Exists(officePath)) return null;
            JObject root;
            try { root = JObject.Parse(File.ReadAllText(officePath)); }
            catch (JsonException ex)
            {
                throw new ConfigException("The office settings file " + officePath + " is not valid JSON: " + ex.Message, ex);
            }
            return root.GetValue(AdminKey, StringComparison.OrdinalIgnoreCase) as JObject;
        }

        public static bool HasPin(string officePath)
        {
            return HasPin(ReadAdmin(officePath));
        }

        public static bool VerifyPin(string officePath, string pin)
        {
            return Verify(ReadAdmin(officePath), pin);
        }

        /// <summary>
        /// Why the office file cannot be written from here, or null when it can: the folder
        /// must exist and accept a new file, and an existing office file must not be read-only
        /// or held open by another program.
        /// </summary>
        public static string WriteProblem(string officePath)
        {
            if (string.IsNullOrWhiteSpace(officePath)) return "FTF cannot tell which folder it was loaded from.";

            string dir;
            try { dir = Path.GetDirectoryName(officePath); }
            catch (ArgumentException ex) { return ex.Message; }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return "The folder " + dir + " does not exist.";

            try
            {
                if (File.Exists(officePath))
                {
                    if ((File.GetAttributes(officePath) & FileAttributes.ReadOnly) != 0)
                        return officePath + " is read-only.";
                    using (new FileStream(officePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { }
                }

                var probe = Path.Combine(dir, ".ftf-write-check-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return null;
            }
            catch (UnauthorizedAccessException ex) { return "No permission to write in " + dir + ": " + ex.Message; }
            catch (IOException ex) { return "Cannot write in " + dir + ": " + ex.Message; }
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
