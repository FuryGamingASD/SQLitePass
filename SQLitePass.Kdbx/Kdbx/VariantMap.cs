using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace DgNet.Keepass;

// Binary key-value map used by KDBX 4.x to store KDF parameters.
// Format: [version (1)] { [type (1)][keyLen (4 LE)][key UTF8][valLen (4 LE)][val] }* [0x00]
public class VariantMap {

	private readonly Dictionary<string, object> _data;

	internal VariantMap(Dictionary<string, object> data) => _data = data;

	public static VariantMap Read(byte[] data) {
		var map = new Dictionary<string, object>();
		using var reader = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
		try {
			ushort version = reader.ReadUInt16();
			if (version != 0x0100)
				throw new FormatException($"Unsupported VariantMap version: 0x{version:X4}");

			while (true) {
				byte type = reader.ReadByte();
				if (type == 0x00) break;

				byte[] keyBytes = ReadLengthPrefixedBytes(reader);
				byte[]? val = null;
				bool retained = false;
				try {
					string key = Encoding.UTF8.GetString(keyBytes);
					val = ReadLengthPrefixedBytes(reader);
					object parsed = type switch {
						0x04 => BinaryPrimitives.ReadUInt32LittleEndian(val),
						0x05 => BinaryPrimitives.ReadUInt64LittleEndian(val),
						0x08 => val[0] != 0,
						0x0C => BinaryPrimitives.ReadInt32LittleEndian(val),
						0x0D => BinaryPrimitives.ReadInt64LittleEndian(val),
						0x18 => Encoding.UTF8.GetString(val),
						0x42 => val,
						_ => throw new NotSupportedException($"Unknown variant map type: 0x{type:X2}")
					};
					if (type == 0x42) retained = true;
					if (map.TryGetValue(key, out var previous) && previous is byte[] previousBytes)
						CryptographicOperations.ZeroMemory(previousBytes);
					map[key] = parsed;
				} finally {
					CryptographicOperations.ZeroMemory(keyBytes);
					if (!retained && val != null) CryptographicOperations.ZeroMemory(val);
				}
			}

			return new VariantMap(map);
		} catch {
			foreach (var value in map.Values)
				if (value is byte[] bytes) CryptographicOperations.ZeroMemory(bytes);
			throw;
		}
	}

	private static byte[] ReadLengthPrefixedBytes(BinaryReader reader) {
		uint length = reader.ReadUInt32();
		long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
		if (length > 1024 * 1024 || length > remaining)
			throw new InvalidDataException("KDBX variant-map field length is invalid.");
		byte[] value = reader.ReadBytes((int)length);
		if (value.Length != length) {
			CryptographicOperations.ZeroMemory(value);
			throw new EndOfStreamException("Unexpected end of KDBX variant-map field.");
		}
		return value;
	}

	internal byte[] Serialize() {
		using var ms     = new MemoryStream();
		using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

		writer.Write((ushort)0x0100); // version

		foreach (var (key, value) in _data) {
			byte   type;
			byte[] val;

			switch (value) {
				case uint   u: type = 0x04; val = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(val, u);  break;
				case ulong  u: type = 0x05; val = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(val, u);  break;
				case bool   b: type = 0x08; val = [b ? (byte)1 : (byte)0];                                        break;
				case int    i: type = 0x0C; val = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(val, i);   break;
				case long   l: type = 0x0D; val = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(val, l);   break;
				case string s: type = 0x18; val = Encoding.UTF8.GetBytes(s);                                      break;
				case byte[] b: type = 0x42; val = b;                                                              break;
				default: throw new NotSupportedException($"Unsupported variant type: {value.GetType()}");
			}

			byte[] keyBytes = Encoding.UTF8.GetBytes(key);
			writer.Write(type);
			writer.Write((uint)keyBytes.Length);
			writer.Write(keyBytes);
			writer.Write((uint)val.Length);
			writer.Write(val);
		}

		writer.Write((byte)0x00); // terminator
		writer.Flush();
		return ms.ToArray();
	}

	public bool TryGetValue(string key, out object? value) => _data.TryGetValue(key, out value);

	internal void ZeroizeByteArrays() {
		foreach (var value in _data.Values)
			if (value is byte[] bytes) CryptographicOperations.ZeroMemory(bytes);
	}

	public object this[string key] => _data[key];

	public string Dump() {
		var sb = new System.Text.StringBuilder();
		foreach (var (k, v) in _data) {
			string display = v switch {
				byte[] b => $"[{b.Length} bytes] {BitConverter.ToString(b)}",
				_        => v.ToString() ?? "(null)"
			};
			sb.AppendLine($"  {k} = {display}");
		}
		return sb.ToString();
	}
}
