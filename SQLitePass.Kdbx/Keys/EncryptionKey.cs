using System;
using System.Security.Cryptography;
using System.Threading;

namespace DgNet.Keepass;

public sealed class EncryptionKey : IDisposable {

	private byte[]? _key;
	private byte[]? _hmacKey;

	public EncryptionKey(byte[] masterSeed, DerivedKey derivedKey) {
		ArgumentNullException.ThrowIfNull(masterSeed);
		ArgumentNullException.ThrowIfNull(derivedKey);
		byte[] derived = derivedKey.GetRawKey();
		byte[] buf = new byte[masterSeed.Length + derived.Length];
		byte[]? bufHmac = null;
		byte[]? key = null;
		byte[]? hmacKey = null;
		try {
			masterSeed.CopyTo(buf, 0);
			derived.CopyTo(buf, masterSeed.Length);
			key = SHA256.HashData(buf);

			// SHA512(masterSeed ∥ derivedKey ∥ 0x01) — KDBX 4 HMAC key
			bufHmac = new byte[masterSeed.Length + derived.Length + 1];
			masterSeed.CopyTo(bufHmac, 0);
			derived.CopyTo(bufHmac, masterSeed.Length);
			bufHmac[^1] = 0x01;
			hmacKey = SHA512.HashData(bufHmac);

			_key = key;
			_hmacKey = hmacKey;
			key = null;
			hmacKey = null;
		} finally {
			CryptographicOperations.ZeroMemory(buf);
			if (bufHmac != null) CryptographicOperations.ZeroMemory(bufHmac);
			if (key != null) CryptographicOperations.ZeroMemory(key);
			if (hmacKey != null) CryptographicOperations.ZeroMemory(hmacKey);
		}
	}

	// 32-byte key used to encrypt/decrypt the payload (AES-CBC, ChaCha20, Twofish)
	public byte[] GetKey() => _key ?? throw new ObjectDisposedException(nameof(EncryptionKey));

	// 64-byte key used for HMAC-SHA512 block integrity (KDBX 4 only)
	public byte[] GetHmacKey() => _hmacKey ?? throw new ObjectDisposedException(nameof(EncryptionKey));

	public void Dispose() {
		byte[]? key = Interlocked.Exchange(ref _key, null);
		byte[]? hmacKey = Interlocked.Exchange(ref _hmacKey, null);
		if (key != null) CryptographicOperations.ZeroMemory(key);
		if (hmacKey != null) CryptographicOperations.ZeroMemory(hmacKey);
	}
}
