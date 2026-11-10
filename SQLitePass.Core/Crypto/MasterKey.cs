using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SQLitePass.Core.Crypto
{
    /// <summary>
    /// Represents the composite master key (Master Password + optional Key File).
    /// </summary>
    public sealed class MasterKey : IDisposable
    {
        private byte[] _compositeKeyHash;
        private bool _disposed;

        public MasterKey(string password, byte[]? keyFileBytes = null)
        {
            if (string.IsNullOrEmpty(password) && (keyFileBytes == null || keyFileBytes.Length == 0))
            {
                throw new ArgumentException("A master password or a key file must be provided.");
            }

            _compositeKeyHash = ComputeCompositeKey(password, keyFileBytes);
        }

        public byte[] GetKeySeed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var copy = new byte[_compositeKeyHash.Length];
            Array.Copy(_compositeKeyHash, copy, _compositeKeyHash.Length);
            return copy;
        }

        private static byte[] ComputeCompositeKey(string password, byte[]? keyFileBytes)
        {
            using var sha = SHA256.Create();

            byte[]? pwHash = null;
            if (!string.IsNullOrEmpty(password))
            {
                byte[] pwBytes = Encoding.UTF8.GetBytes(password);
                pwHash = sha.ComputeHash(pwBytes);
                CryptographicOperations.ZeroMemory(pwBytes);
            }

            byte[]? kfHash = null;
            if (keyFileBytes != null && keyFileBytes.Length > 0)
            {
                // If it's a 32-byte binary key file, use directly or hash it
                kfHash = sha.ComputeHash(keyFileBytes);
            }

            // Combine both hashes
            if (pwHash != null && kfHash != null)
            {
                byte[] combined = new byte[pwHash.Length + kfHash.Length];
                Buffer.BlockCopy(pwHash, 0, combined, 0, pwHash.Length);
                Buffer.BlockCopy(kfHash, 0, combined, pwHash.Length, kfHash.Length);
                byte[] result = sha.ComputeHash(combined);
                CryptographicOperations.ZeroMemory(pwHash);
                CryptographicOperations.ZeroMemory(kfHash);
                CryptographicOperations.ZeroMemory(combined);
                return result;
            }
            else if (pwHash != null)
            {
                return pwHash;
            }
            else if (kfHash != null)
            {
                return kfHash;
            }

            throw new InvalidOperationException("Failed to generate master key.");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_compositeKeyHash != null)
                {
                    CryptographicOperations.ZeroMemory(_compositeKeyHash);
                    _compositeKeyHash = Array.Empty<byte>();
                }
                _disposed = true;
            }
        }
    }
}
