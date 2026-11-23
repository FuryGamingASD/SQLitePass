using System;
using System.Security.Cryptography;
using System.Threading;

namespace DgNet.Keepass;

public sealed class DerivedKey : IDisposable {

	private byte[]? _key;

	private DerivedKey(byte[] key) => _key = key;

	public static DerivedKey Derive(CompositeKey compositeKey, IKdf kdf) {
		ArgumentNullException.ThrowIfNull(compositeKey);
		ArgumentNullException.ThrowIfNull(kdf);
		byte[] rawKey = compositeKey.GetRawKey();
		byte[]? transformed = null;
		try {
			transformed = kdf.Transform(rawKey);
			var result = new DerivedKey(transformed);
			transformed = null;
			return result;
		} finally {
			CryptographicOperations.ZeroMemory(rawKey);
			if (transformed != null) CryptographicOperations.ZeroMemory(transformed);
		}
	}

	public byte[] GetRawKey() => _key ?? throw new ObjectDisposedException(nameof(DerivedKey));

	public void Dispose() {
		byte[]? key = Interlocked.Exchange(ref _key, null);
		if (key != null) CryptographicOperations.ZeroMemory(key);
	}
}
