using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace DgNet.Keepass;

public class AesKdf : IKdf, IDisposable {

	public static readonly Guid Uuid = new("c9d9f39a-628a-4460-bf74-0d08c18a4fea");

	public byte[] Seed   { get; }
	public ulong  Rounds { get; }

	public AesKdf(byte[] seed, ulong rounds) {
		Seed   = seed;
		Rounds = rounds;
	}

	// DerivedKey = SHA256(ECB(left, seed, rounds) ∥ ECB(right, seed, rounds))
	// where left = rawKey[0..16], right = rawKey[16..32]
	public byte[] Transform(byte[] rawKey) {
		if (rawKey.Length != 32) throw new ArgumentException("AES-KDF input must be 32 bytes.", nameof(rawKey));
		byte[]? left = null;
		byte[]? right = null;
		byte[]? leftBuf = null;
		byte[]? rightBuf = null;
		byte[]? combined = null;
		try {
			left = rawKey[..16];
			right = rawKey[16..];
			leftBuf = new byte[16];
			rightBuf = new byte[16];
			using var aes = Aes.Create();
			aes.Mode = CipherMode.ECB;
			aes.Padding = PaddingMode.None;
			aes.Key = Seed;

			using var encryptor = aes.CreateEncryptor();
			for (ulong i = 0; i < Rounds; i++) {
				encryptor.TransformBlock(left,  0, 16, leftBuf,  0);
				encryptor.TransformBlock(right, 0, 16, rightBuf, 0);
				(left, leftBuf)   = (leftBuf,  left);
				(right, rightBuf) = (rightBuf, right);
			}

			combined = new byte[32];
			left.CopyTo(combined, 0);
			right.CopyTo(combined, 16);
			return SHA256.HashData(combined);
		} finally {
			if (left != null) CryptographicOperations.ZeroMemory(left);
			if (right != null) CryptographicOperations.ZeroMemory(right);
			if (leftBuf != null) CryptographicOperations.ZeroMemory(leftBuf);
			if (rightBuf != null) CryptographicOperations.ZeroMemory(rightBuf);
			if (combined != null) CryptographicOperations.ZeroMemory(combined);
		}
	}

	public void Dispose() => CryptographicOperations.ZeroMemory(Seed);

	public VariantMap Parameters() => new(new Dictionary<string, object> {
		["$UUID"] = GuidRfc4122.ToBytes(Uuid),
		["S"]     = (byte[])Seed.Clone(),
		["R"]     = Rounds,
	});
}
