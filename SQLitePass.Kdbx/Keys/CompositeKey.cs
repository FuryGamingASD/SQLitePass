using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace DgNet.Keepass;

// Composite key = SHA256(component₁ ∥ component₂ ∥ …)
// Each component is 32 bytes:
//   password  → SHA256(UTF8(password))
//   key file  → 32-byte key extracted from the file (XML, hex, raw, or SHA256 fallback)
public class CompositeKey {

	private readonly List<byte[]> _components = [];

	public CompositeKey() {}

	public CompositeKey(string password) {
		if (!string.IsNullOrEmpty(password)) AddPassword(password);
	}

	public CompositeKey(string password, string keyFile) {
		if (!string.IsNullOrEmpty(password)) AddPassword(password);
		AddKeyFile(keyFile);
	}

	public CompositeKey AddPassword(string password) {
		_components.Add(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
		return this;
	}

	public CompositeKey AddKeyFile(string path) {
		byte[] data = File.ReadAllBytes(path);
		try { _components.Add(ReadKeyFile(data)); }
		finally { CryptographicOperations.ZeroMemory(data); }
		return this;
	}

	public CompositeKey AddKeyFile(ReadOnlySpan<byte> contents) {
		_components.Add(ReadKeyFile(contents));
		return this;
	}

	internal void Zeroize() {
		foreach (var c in _components)
			Array.Clear(c);
		_components.Clear();
	}

	public byte[] GetRawKey() {
		if (_components.Count == 0)
			throw new InvalidOperationException("CompositeKey has no components.");

		var buffer = new byte[_components.Count * 32];
		int offset = 0;
		foreach (var c in _components) {
			c.CopyTo(buffer, offset);
			offset += 32;
		}
		return SHA256.HashData(buffer);
	}

	private static byte[] ReadKeyFile(ReadOnlySpan<byte> data) {
		if (TryParseXmlKeyFile(data, out var xmlKey))
			return xmlKey!;

		// 64 ASCII hex chars → 32 bytes
		if (data.Length == 64 && TryParseHex(data, out var hexKey))
			return hexKey!;

		// Raw 32-byte binary key
		if (data.Length == 32)
			return data.ToArray();

		return SHA256.HashData(data);
	}

	private static bool TryParseXmlKeyFile(ReadOnlySpan<byte> data, out byte[]? key) {
		key = null;
		try {
			var doc = XDocument.Parse(Encoding.UTF8.GetString(data));
			var dataElement = doc.Root?.Element("Key")?.Element("Data");
			if (dataElement is null) return false;
			key = Convert.FromBase64String(dataElement.Value.Trim());
			return key.Length == 32;
		} catch {
			return false;
		}
	}

	private static bool TryParseHex(ReadOnlySpan<byte> data, out byte[]? key) {
		key = null;
		try {
			key = Convert.FromHexString(Encoding.ASCII.GetString(data).Trim());
			return key.Length == 32;
		} catch {
			return false;
		}
	}
}
