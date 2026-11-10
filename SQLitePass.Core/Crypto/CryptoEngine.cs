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

        /// <summary>
        /// Encrypts raw bytes using AES-256-GCM.
        /// Output format: [12 bytes Nonce] + [16 bytes Tag] + [Ciphertext]
        /// </summary>
        public byte[] EncryptBytes(byte[]? plainBytes)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (plainBytes == null || plainBytes.Length == 0)
                return Array.Empty<byte>();

            byte[] nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);

            byte[] tag = new byte[TagSize];
            byte[] cipherText = new byte[plainBytes.Length];

            using var aesGcm = new AesGcm(_symmetricKey, TagSize);
            aesGcm.Encrypt(nonce, plainBytes, cipherText, tag);

            byte[] result = new byte[NonceSize + TagSize + cipherText.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
            Buffer.BlockCopy(cipherText, 0, result, NonceSize + TagSize, cipherText.Length);

            return result;
        }

        /// <summary>
        /// Decrypts raw bytes previously encrypted with EncryptBytes.
        /// </summary>
        public byte[] DecryptBytes(byte[]? encryptedPayload)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (encryptedPayload == null || encryptedPayload.Length == 0)
                return Array.Empty<byte>();

            if (encryptedPayload.Length < NonceSize + TagSize)
                throw new CryptographicException("Encrypted payload is corrupted or too short.");

            byte[] nonce = new byte[NonceSize];
            byte[] tag = new byte[TagSize];
            int cipherTextLength = encryptedPayload.Length - NonceSize - TagSize;
            byte[] cipherText = new byte[cipherTextLength];

            Buffer.BlockCopy(encryptedPayload, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(encryptedPayload, NonceSize, tag, 0, TagSize);
            Buffer.BlockCopy(encryptedPayload, NonceSize + TagSize, cipherText, 0, cipherTextLength);

            byte[] plainBytes = new byte[cipherTextLength];
            using var aesGcm = new AesGcm(_symmetricKey, TagSize);
            aesGcm.Decrypt(nonce, cipherText, tag, plainBytes);

            return plainBytes;
        }

        /// <summary>
        /// Encrypts a string and returns a Base64-encoded string.
        /// </summary>
        public string EncryptString(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            try
            {
                byte[] encrypted = EncryptBytes(plainBytes);
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
        public string DecryptString(string? base64Payload)
        {
            if (string.IsNullOrEmpty(base64Payload))
                return string.Empty;

            byte[] encrypted = Convert.FromBase64String(base64Payload);
            byte[] decrypted = DecryptBytes(encrypted);
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
            catch (CryptographicException)
            {
                return false;
            }
            catch (Exception)
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
