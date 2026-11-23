using System;
using System.Security.Cryptography;
using System.Threading;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace DgNet.Keepass;

// Inner stream algorithm IDs as stored in the KDBX header (InnerRandomStreamID field)
public enum ProtectedStreamAlgorithm { Salsa20 = 2, ChaCha20 = 3 }

// Stateful XOR stream used to encrypt/decrypt Protected="True" XML fields.
// The keystream position advances with each call to Process() — fields must be
// processed in the same order during read and write.
public sealed class ProtectedStream : IDisposable {

	private readonly IStreamCipher _cipher;
	private int _disposed;

	public ProtectedStreamAlgorithm Algorithm { get; }
	public byte[] Key { get; }

	public ProtectedStream(ProtectedStreamAlgorithm algorithm, byte[] key) {
		Algorithm = algorithm;
		Key       = key;
		try {
			_cipher = algorithm switch {
				ProtectedStreamAlgorithm.Salsa20  => InitSalsa20(key),
				ProtectedStreamAlgorithm.ChaCha20 => InitChaCha20(key),
				_ => throw new NotSupportedException($"Unknown protected stream algorithm: {algorithm}")
			};
		} catch {
			CryptographicOperations.ZeroMemory(Key);
			throw;
		}
	}

	public void Dispose() {
		if (Interlocked.Exchange(ref _disposed, 1) == 0) CryptographicOperations.ZeroMemory(Key);
	}

	public static int GetIvSize(CipherAlgorithm cipher) => cipher == CipherAlgorithm.ChaCha20 ? 12 : 16;

	// XOR data with the next keystream bytes (same operation for encrypt and decrypt)
	public byte[] Process(byte[] data) {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		var output = new byte[data.Length];
		_cipher.ProcessBytes(data, 0, data.Length, output, 0);
		return output;
	}

	private static IStreamCipher InitSalsa20(byte[] key) {
		var engine = new Salsa20Engine();
		byte[] derivedKey = SHA256.HashData(key);
		try {
			engine.Init(true, new ParametersWithIV(
				new KeyParameter(derivedKey),
				new byte[] { 0xE8, 0x30, 0x09, 0x4B, 0x97, 0x20, 0x5D, 0x2A }
			));
			return engine;
		} finally { CryptographicOperations.ZeroMemory(derivedKey); }
	}

	private static IStreamCipher InitChaCha20(byte[] key) {
		var hash = SHA512.HashData(key);
		byte[]? derivedKey = null;
		byte[]? nonce = null;
		try {
			derivedKey = hash[..32];
			nonce = hash[32..44];
			var engine = new ChaCha7539Engine();
			engine.Init(true, new ParametersWithIV(new KeyParameter(derivedKey), nonce));
			return engine;
		} finally {
			CryptographicOperations.ZeroMemory(hash);
			if (derivedKey != null) CryptographicOperations.ZeroMemory(derivedKey);
			if (nonce != null) CryptographicOperations.ZeroMemory(nonce);
		}
	}
}
