using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace DgNet.Keepass;

public class KdbxReader {
	private const int MaxCiphertextBytes = 128 * 1024 * 1024;
	private const int MaxPlaintextBytes = 256 * 1024 * 1024;
	private const int MaxCipherBlockBytes = 16 * 1024 * 1024;
	private const int MaxInnerHeaderFieldBytes = 64 * 1024 * 1024;
	private const int MaxKdbxBlocks = 65536;

	private readonly Database _db;

	public KdbxReader(Database db) {
		_db = db;
	}

	public void ReadFrom(Stream stream) {
		// RecordingStream captures header bytes needed for KDBX 4.x verification
		using var recording = new RecordingStream(stream);
		KdbxHeader? header = null;
		byte[]? headerBytes = null;
		try {
			header = KdbxHeader.Read(new BinaryReader(recording));
			headerBytes = recording.GetRecordedBytes();
			_db.Version = header.Version;

			var kdf = header.CreateKdf();
			using var kdfLifetime = kdf as IDisposable;
			using var derived = DerivedKey.Derive(_db.Key, kdf);
			using var encKey = new EncryptionKey(header.MasterSeed, derived);

			if (header.IsVersion4) ReadV4(header, encKey, headerBytes, stream);
			else                   ReadV3(header, encKey, stream);
		} finally {
			if (headerBytes != null) CryptographicOperations.ZeroMemory(headerBytes);
			header?.ZeroizeSensitiveData();
		}
	}

	// ── KDBX 3.x ────────────────────────────────────────────────────────────────
	// File: [Header][SymmetricCipher([StreamStartBytes(32)][HashedBlockStream][XML])]

	private void ReadV3(KdbxHeader header, EncryptionKey encKey, Stream stream) {
		using var cipher = header.CreateCipher(encKey.GetKey());
		using var decrypted = cipher.CreateDecryptingStream(stream);
		var reader = new BinaryReader(decrypted);

		// First 32 bytes of the decrypted stream must match StreamStartBytes from header
		byte[] startBytes = ReadExactBytes(reader, 32, "KDBX stream-start bytes");
		try {
			if (!startBytes.SequenceEqual(header.StreamStartBytes!))
				throw new InvalidDataException("StreamStartBytes verification failed.");
		} finally { CryptographicOperations.ZeroMemory(startBytes); }

		byte[] plaintext = ReadHashedBlocks(reader);
		try {
			if (header.IsCompressed) {
				byte[] expanded = Decompress(plaintext);
				CryptographicOperations.ZeroMemory(plaintext);
				plaintext = expanded;
			}

			using var ps = new ProtectedStream(header.InnerRandomStreamId, header.ProtectedStreamKey!);
			_db.Settings = Settings.FromHeader(header, header.InnerRandomStreamId);
			using var xmlStream = new MemoryStream(plaintext);
			new KdbxXmlReader(_db, ps, isV4: false).ReadFrom(xmlStream);
		}
		finally { CryptographicOperations.ZeroMemory(plaintext); }
	}

	// ── KDBX 4.x ────────────────────────────────────────────────────────────────
	// File: [Header][SHA256(32)][HMAC(32)][HmacBlockStream([SymmetricCipher([InnerHeader][XML])])]

	private void ReadV4(KdbxHeader header, EncryptionKey encKey, byte[] headerBytes, Stream stream) {
		var fileReader = new BinaryReader(stream);

		// Verify SHA256 of the outer header
		byte[] sha = ReadExactBytes(fileReader, 32, "KDBX header digest");
		byte[] expectedSha = SHA256.HashData(headerBytes);
		try {
			if (!sha.SequenceEqual(expectedSha))
				throw new InvalidDataException("Header SHA256 verification failed.");
		} finally {
			CryptographicOperations.ZeroMemory(sha);
			CryptographicOperations.ZeroMemory(expectedSha);
		}

		// Verify HMAC-SHA256 of the outer header (block key uses index = UInt64.MaxValue)
		byte[] headerHmac    = ReadExactBytes(fileReader, 32, "KDBX header HMAC");
		byte[] headerHmacKey = BlockKey(ulong.MaxValue, encKey.GetHmacKey());
		try {
			using var h = new HMACSHA256(headerHmacKey);
			byte[] expectedHmac = h.ComputeHash(headerBytes);
			try {
				if (!headerHmac.SequenceEqual(expectedHmac))
					throw new InvalidDataException("Header HMAC verification failed.");
			} finally { CryptographicOperations.ZeroMemory(expectedHmac); }
		} finally {
			CryptographicOperations.ZeroMemory(headerHmac);
			CryptographicOperations.ZeroMemory(headerHmacKey);
		}

		// Read and verify the HmacBlockStream → ciphertext
		byte[] ciphertext = ReadHmacBlocks(fileReader, encKey.GetHmacKey(), MaxCiphertextBytes);
		try {
			if (stream.ReadByte() != -1)
				throw new InvalidDataException("Unexpected data follows the KDBX block stream.");

			using var cipher = header.CreateCipher(encKey.GetKey());
			using var cipherInput = new MemoryStream(ciphertext);
			using var decryptedStream = cipher.CreateDecryptingStream(cipherInput);

			// Copy decrypted (and optionally decompressed) bytes into a seekable MemoryStream
			using var plainStream = new MemoryStream();
			try {
				if (header.IsCompressed) {
					using var gzip = new GZipStream(decryptedStream, CompressionMode.Decompress);
					CopyBounded(gzip, plainStream, MaxPlaintextBytes);
				} else {
					CopyBounded(decryptedStream, plainStream, MaxPlaintextBytes);
				}
				plainStream.Position = 0;
				var (algo, innerKey, binaries) = ReadInnerHeader(new BinaryReader(plainStream));
				try {
					using var ps = new ProtectedStream(algo, innerKey);
					_db.Settings = Settings.FromHeader(header, algo);
					new KdbxXmlReader(_db, ps, isV4: true, binaries).ReadFrom(plainStream);
				} catch {
					foreach (var binary in binaries) CryptographicOperations.ZeroMemory(binary.Data);
					throw;
				} finally { CryptographicOperations.ZeroMemory(innerKey); }
			} finally {
				CryptographicOperations.ZeroMemory(plainStream.GetBuffer().AsSpan(0, checked((int)plainStream.Length)));
			}
		}
		finally { CryptographicOperations.ZeroMemory(ciphertext); }
	}

	// ── HashedBlockStream (KDBX 3.x) ────────────────────────────────────────────
	// Block: [Index (4 LE)][SHA256 (32)][Size (4 LE)][Data]
	// Terminates when Size == 0.

	private static byte[] ReadHashedBlocks(BinaryReader reader) {
		using var result = new MemoryStream();
		try {
			int blockCount = 0;
			while (true) {
				if (++blockCount > MaxKdbxBlocks) throw new InvalidDataException("KDBX block count exceeds the supported limit.");
				reader.ReadUInt32();           // block index (sequential, not verified here)
				byte[] hash = ReadExactBytes(reader, 32, "KDBX block hash");
				try {
					uint encodedSize = reader.ReadUInt32();
					if (encodedSize > MaxCipherBlockBytes || encodedSize > MaxCiphertextBytes - result.Length)
						throw new InvalidDataException("KDBX block size exceeds the supported limit.");
					int size = (int)encodedSize;
					byte[] data = ReadExactBytes(reader, size, "KDBX block");
					try {
						if (size == 0) break;
						byte[] expectedHash = SHA256.HashData(data);
						try {
							if (!expectedHash.SequenceEqual(hash)) throw new InvalidDataException("Block hash verification failed.");
						} finally { CryptographicOperations.ZeroMemory(expectedHash); }
						result.Write(data);
					} finally { CryptographicOperations.ZeroMemory(data); }
				} finally { CryptographicOperations.ZeroMemory(hash); }
			}
			return result.ToArray();
		} finally {
			if (result.TryGetBuffer(out var buffer)) CryptographicOperations.ZeroMemory(buffer.AsSpan(0, checked((int)result.Length)));
		}
	}

	// ── HmacBlockStream (KDBX 4.x) ──────────────────────────────────────────────
	// Block: [HMAC-SHA256 (32)][Size (4 LE signed)][Data]
	// Terminates when Size == 0 (last block is still HMAC-verified).

	private static byte[] ReadHmacBlocks(BinaryReader reader, byte[] hmacKey64, int maximumBytes) {
		using var result = new MemoryStream();
		try {
			int blockCount = 0;
			ulong blockIndex = 0;
			while (true) {
				if (++blockCount > MaxKdbxBlocks)
					throw new InvalidDataException("KDBX block count exceeds the supported limit.");
				byte[] hmac = ReadExactBytes(reader, 32, "KDBX block HMAC");
				try {
					int size = reader.ReadInt32();
					if (size < 0 || size > MaxCipherBlockBytes || size > maximumBytes - result.Length)
						throw new InvalidDataException("KDBX encrypted block size exceeds the supported limit.");
					byte[] data = ReadExactBytes(reader, size, "KDBX encrypted block");
					try {
						byte[] blockKey = BlockKey(blockIndex, hmacKey64);
						try {
							byte[] expectedHmac = BlockHmac(blockIndex, size, data, blockKey);
							try {
								if (!hmac.SequenceEqual(expectedHmac))
									throw new InvalidDataException($"Block {blockIndex} HMAC verification failed.");
							} finally { CryptographicOperations.ZeroMemory(expectedHmac); }
						} finally { CryptographicOperations.ZeroMemory(blockKey); }
						if (size == 0) break;
						result.Write(data);
						blockIndex++;
					} finally { CryptographicOperations.ZeroMemory(data); }
				} finally { CryptographicOperations.ZeroMemory(hmac); }
			}
			return result.ToArray();
		} finally {
			if (result.TryGetBuffer(out var buffer)) CryptographicOperations.ZeroMemory(buffer.AsSpan(0, checked((int)result.Length)));
		}
	}

	// blockKey = SHA512(blockIndex_LE64 ∥ hmacKey64)
	private static byte[] BlockKey(ulong blockIndex, byte[] hmacKey64) {
		var buf = new byte[8 + hmacKey64.Length];
		try {
			BinaryPrimitives.WriteUInt64LittleEndian(buf, blockIndex);
			hmacKey64.CopyTo(buf, 8);
			return SHA512.HashData(buf);
		} finally { CryptographicOperations.ZeroMemory(buf); }
	}

	// blockHmac = HMAC-SHA256(blockIndex_LE64 ∥ blockSize_LE32 ∥ data, blockKey)
	private static byte[] BlockHmac(ulong blockIndex, int blockSize, byte[] data, byte[] blockKey) {
		var msg = new byte[8 + 4 + data.Length];
		BinaryPrimitives.WriteUInt64LittleEndian(msg, blockIndex);
		BinaryPrimitives.WriteInt32LittleEndian(msg.AsSpan(8), blockSize);
		data.CopyTo(msg, 12);
		try {
			using var hmac = new HMACSHA256(blockKey);
			return hmac.ComputeHash(msg);
		} finally { CryptographicOperations.ZeroMemory(msg); }
	}

	// ── Decompression ────────────────────────────────────────────────────────────

	private static byte[] Decompress(byte[] data) {
		using var input  = new MemoryStream(data);
		using var gzip   = new GZipStream(input, CompressionMode.Decompress);
		using var output = new MemoryStream();
		try {
			CopyBounded(gzip, output, MaxPlaintextBytes);
			return output.ToArray();
		} finally {
			if (output.TryGetBuffer(out var buffer)) CryptographicOperations.ZeroMemory(buffer.AsSpan(0, checked((int)output.Length)));
		}
	}

	private static void CopyBounded(Stream source, Stream destination, int maximumBytes) {
		byte[] buffer = new byte[81920];
		try {
			int read;
			while ((read = source.Read(buffer, 0, buffer.Length)) != 0) {
				if (destination.Length > maximumBytes - read)
					throw new InvalidDataException("KDBX decompressed data exceeds the supported limit.");
				destination.Write(buffer, 0, read);
			}
		} finally {
			CryptographicOperations.ZeroMemory(buffer);
		}
	}

	private static byte[] ReadExactBytes(BinaryReader reader, int count, string description) {
		if (count < 0 || count > MaxPlaintextBytes ||
			(reader.BaseStream.CanSeek && count > reader.BaseStream.Length - reader.BaseStream.Position))
			throw new InvalidDataException($"{description} length is invalid.");
		byte[] bytes = reader.ReadBytes(count);
		if (bytes.Length != count) {
			CryptographicOperations.ZeroMemory(bytes);
			throw new EndOfStreamException($"Unexpected end of {description}.");
		}
		return bytes;
	}

	// ── Inner header (KDBX 4.x, inside the decrypted payload) ───────────────────
	// Field: [ID (1)][Length (4 LE)][Data]
	// 0x00 = EndOfHeader
	// 0x01 = InnerRandomStreamId
	// 0x02 = InnerRandomStreamKey
	// 0x03 = Binary  →  [flags (1)][data]  (flags bit0 = isProtected, ignored on read)

	private static (ProtectedStreamAlgorithm algo, byte[] key, List<(bool IsProtected, byte[] Data)> binaries) ReadInnerHeader(BinaryReader reader) {
		var    algo     = ProtectedStreamAlgorithm.ChaCha20;
		byte[]? key     = null;
		var    binaries = new List<(bool IsProtected, byte[] Data)>();
		try {
			while (true) {
				byte id = reader.ReadByte();
				uint encodedLength = reader.ReadUInt32();
				if (encodedLength > MaxInnerHeaderFieldBytes || encodedLength > reader.BaseStream.Length - reader.BaseStream.Position)
					throw new InvalidDataException("KDBX inner-header field length is invalid or exceeds the supported limit.");
				int len = (int)encodedLength;
				byte[] data = ReadExactBytes(reader, len, "KDBX inner-header field");
				bool retained = false;
				try {
					if (id == 0x00) return (algo, key ?? [], binaries);
					switch (id) {
						case 0x01:
							if (data.Length != 4) throw new InvalidDataException("KDBX inner-header stream algorithm has an invalid length.");
						algo = (ProtectedStreamAlgorithm)BinaryPrimitives.ReadUInt32LittleEndian(data);
						break;
						case 0x02:
							if (key != null) CryptographicOperations.ZeroMemory(key);
						key = data;
						retained = true;
						break;
						case 0x03:
							// byte 0 = flags (bit0 = isProtected); remaining bytes = raw data
							if (data.Length == 0) throw new InvalidDataException("KDBX inner-header binary is missing its protection flag.");
						binaries.Add((IsProtected: (data[0] & 0x01) != 0, Data: data[1..]));
						break;
					}
				} finally {
					if (!retained) CryptographicOperations.ZeroMemory(data);
				}
			}
		} catch {
			if (key != null) CryptographicOperations.ZeroMemory(key);
			foreach (var binary in binaries) CryptographicOperations.ZeroMemory(binary.Data);
			throw;
		}
	}

	// ── RecordingStream ──────────────────────────────────────────────────────────
	// Wraps a stream and records every byte read through it.
	// Does NOT dispose the inner stream.

	private sealed class RecordingStream : Stream {

		private readonly Stream _inner;
		private readonly MemoryStream _buffer = new();

		public RecordingStream(Stream inner) => _inner = inner;

		public byte[] GetRecordedBytes() => _buffer.ToArray();

		public override bool CanRead  => _inner.CanRead;
		public override bool CanWrite => false;
		public override bool CanSeek  => false;
		public override long Length   => throw new NotSupportedException();
		public override long Position {
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count) {
			int n = _inner.Read(buffer, offset, count);
			_buffer.Write(buffer, offset, n);
			return n;
		}

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override void Flush()                                       => _inner.Flush();
		public override long Seek(long offset, SeekOrigin origin)          => throw new NotSupportedException();
		public override void SetLength(long value)                         => throw new NotSupportedException();

		protected override void Dispose(bool disposing) {
			if (disposing) {
				if (_buffer.TryGetBuffer(out var buffer))
					CryptographicOperations.ZeroMemory(buffer.AsSpan(0, checked((int)_buffer.Length)));
				_buffer.Dispose();
			}
			base.Dispose(disposing); // intentionally does NOT dispose _inner
		}
	}
}
