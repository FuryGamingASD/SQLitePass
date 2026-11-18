using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace SQLitePass.Core.Crypto
{
    /// <summary>
    /// Handles zero-knowledge encryption and decryption using Argon2id key derivation and AES-256-GCM.
    /// </summary>
    public sealed class CryptoEngine : IDisposable
    {
        public const string CanaryVerificationToken = "KEEPASS_MYSQL_VERIFICATION_TOKEN_V1";
        public const int NonceSize = 12; // 96 bits standard for AES-GCM
        public const int TagSize = 16;   // 128 bits authentication tag
        public const int KeySize = 32;   // 256 bits AES key

        private byte[] _symmetricKey;
        private bool _disposed;
        public bool IsDisposed => _disposed;
        // Null denotes a raw imported symmetric key, whose credential factors are unknown.
        public bool? DerivedWithKeyFile { get; private set; }

        public static CryptoEngine FromMasterKey(MasterKey masterKey, byte[] salt, int iterations = 3, int memoryKb = 65536, int parallelism = 2)
        {
            byte[] key = DeriveKeyArgon2(masterKey, salt, iterations, memoryKb, parallelism);
            try { return new CryptoEngine(key) { DerivedWithKeyFile = masterKey.UsesKeyFile }; }
            finally { CryptographicOperations.ZeroMemory(key); }
        }

        public CryptoEngine(byte[] symmetricKey)
        {
            if (symmetricKey == null || symmetricKey.Length != KeySize)
            {
                throw new ArgumentException($"Symmetric key must be {KeySize} bytes (256-bit).", nameof(symmetricKey));
            }

            _symmetricKey = new byte[KeySize];
            Array.Copy(symmetricKey, _symmetricKey, KeySize);
        }

        /// <summary>
        /// Derives a 256-bit symmetric encryption key from a MasterKey and Salt using Argon2id.
        /// </summary>
        public static byte[] DeriveKeyArgon2(MasterKey masterKey, byte[] salt, int iterations = 3, int memoryKb = 64 * 1024, int parallelism = 2)
        {
            ValidateKdf(salt, iterations, memoryKb, parallelism);
            byte[] seed = masterKey.GetKeySeed();
            try
            {
                using var argon2 = new Argon2id(seed)
                {
                    Salt = salt,
                    Iterations = iterations,
                    MemorySize = memoryKb,
                    DegreeOfParallelism = parallelism
                };

                return argon2.GetBytes(KeySize);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(seed);
            }
        }

        public static void ValidateKdf(byte[] salt, int iterations, int memoryKb, int parallelism)
        {
            if (salt.Length is < 16 or > 64 || iterations is < 1 or > 20 ||
                memoryKb is < 8192 or > 524288 || parallelism is < 1 or > 16 || memoryKb < 8 * parallelism)
                throw new InvalidDataException("Parametri di derivazione del vault non supportati (massimo 512 MB, 20 iterazioni, 16 thread).");
        }

        /// <summary>
        /// Encrypts raw bytes using AES-256-GCM.
        /// Output format: [12 bytes Nonce] + [16 bytes Tag] + [Ciphertext]
        /// </summary>
        public byte[] EncryptBytes(byte[]? plainBytes, string? context = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if ((plainBytes == null || plainBytes.Length == 0) && context == null)
                return Array.Empty<byte>();
            plainBytes ??= Array.Empty<byte>();

            byte[] result = new byte[NonceSize + TagSize + plainBytes.Length];
            var nonce = result.AsSpan(0, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            using var aesGcm = new AesGcm(_symmetricKey, TagSize);
            aesGcm.Encrypt(nonce, plainBytes, result.AsSpan(NonceSize + TagSize), result.AsSpan(NonceSize, TagSize),
                context == null ? null : Encoding.UTF8.GetBytes(context));
            return result;
        }

        /// <summary>
        /// Decrypts raw bytes previously encrypted with EncryptBytes.
        /// </summary>
        public byte[] DecryptBytes(byte[]? encryptedPayload, string? context = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if ((encryptedPayload == null || encryptedPayload.Length == 0) && context == null)
                return Array.Empty<byte>();
            encryptedPayload ??= Array.Empty<byte>();

            if (encryptedPayload.Length < NonceSize + TagSize)
                throw new CryptographicException("Encrypted payload is corrupted or too short.");

            int cipherTextLength = encryptedPayload.Length - NonceSize - TagSize;
            byte[] plainBytes = new byte[cipherTextLength];
            using var aesGcm = new AesGcm(_symmetricKey, TagSize);
            try { aesGcm.Decrypt(encryptedPayload.AsSpan(0, NonceSize), encryptedPayload.AsSpan(NonceSize + TagSize),
                encryptedPayload.AsSpan(NonceSize, TagSize), plainBytes, context == null ? null : Encoding.UTF8.GetBytes(context)); }
            catch { CryptographicOperations.ZeroMemory(plainBytes); throw; }

            return plainBytes;
        }

        /// <summary>
        /// Encrypts a string and returns a Base64-encoded string.
        /// </summary>
        public string EncryptString(string? plainText, string? context = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrEmpty(plainText) && context == null)
                return string.Empty;

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText ?? string.Empty);
            try
            {
                byte[] encrypted = EncryptBytes(plainBytes, context);
                return Convert.ToBase64String(encrypted);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }

        /// <summary>
        /// Decrypts a Base64-encoded string previously encrypted with EncryptString.
        /// </summary>
        public string DecryptString(string? base64Payload, string? context = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrEmpty(base64Payload) && context == null)
                return string.Empty;

            byte[] encrypted = Convert.FromBase64String(base64Payload ?? string.Empty);
            byte[] decrypted = DecryptBytes(encrypted, context);
            try
            {
                return Encoding.UTF8.GetString(decrypted);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decrypted);
            }
        }

        /// <summary>
        /// Generates the encrypted canary string used to verify if the entered master password is correct.
        /// </summary>
        public string GenerateCanaryToken()
        {
            return EncryptString(CanaryVerificationToken);
        }

        /// <summary>
        /// Verifies whether the master key is valid by attempting to decrypt the canary token.
        /// </summary>
        public bool VerifyCanaryToken(string canaryBase64)
        {
            try
            {
                string decrypted = DecryptString(canaryBase64);
                return decrypted == CanaryVerificationToken;
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_symmetricKey != null)
                {
                    CryptographicOperations.ZeroMemory(_symmetricKey);
                    _symmetricKey = Array.Empty<byte>();
                }
                _disposed = true;
            }
        }
    }
}
