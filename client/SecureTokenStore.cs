using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodeExplainer
{
    internal sealed class SecureTokenStore
    {
        private readonly string _storePath;

        public SecureTokenStore()
        {
            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodeExplainer");
            Directory.CreateDirectory(baseDir);
            _storePath = Path.Combine(baseDir, "auth-state.bin");
        }

        public void Save(StoredSessionState state)
        {
            string json = JsonSerializer.Serialize(state);
            byte[] plaintext = Encoding.UTF8.GetBytes(json);
            byte[] encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_storePath, encrypted);
        }

        public StoredSessionState? Load()
        {
            if (!File.Exists(_storePath))
            {
                return null;
            }

            try
            {
                byte[] encrypted = File.ReadAllBytes(_storePath);
                byte[] plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(plaintext);
                return JsonSerializer.Deserialize<StoredSessionState>(json);
            }
            catch
            {
                return null;
            }
        }

        public void Clear()
        {
            if (File.Exists(_storePath))
            {
                File.Delete(_storePath);
            }
        }
    }
}
