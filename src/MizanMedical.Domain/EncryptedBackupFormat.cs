using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace MizanMedical.Domain;

/// <summary>Streamed authenticated encrypted archive; rejects wrong passphrases,
/// tampering, missing final marker, trailing bytes and unsupported versions.</summary>
public static class EncryptedBackupFormat
{
    private const int ChunkSize = 1024 * 1024;
    private const int Iterations = 600_000;
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("MIZANBACKUP1");

    public static async Task EncryptAsync(Stream source, Stream destination, string password,
        CancellationToken cancellation = default)
    {
        ValidatePassphrase(password);
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations,
            HashAlgorithmName.SHA256, 32);
        try
        {
            await destination.WriteAsync(Header, cancellation);
            await destination.WriteAsync(salt, cancellation);
            await WriteIntAsync(destination, Iterations, cancellation);
            using var aes = new AesGcm(key, 16);
            byte[] plain = new byte[ChunkSize];
            byte[] encrypted = new byte[ChunkSize];
            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            long index = 0;
            int size;
            while ((size = await source.ReadAsync(plain, cancellation)) > 0)
            {
                RandomNumberGenerator.Fill(nonce);
                aes.Encrypt(nonce, plain.AsSpan(0, size), encrypted.AsSpan(0, size),
                    tag, AssociatedData(index, false));
                await WriteIntAsync(destination, size, cancellation);
                await destination.WriteAsync(nonce, cancellation);
                await destination.WriteAsync(tag, cancellation);
                await destination.WriteAsync(encrypted.AsMemory(0, size), cancellation);
                index++;
            }
            RandomNumberGenerator.Fill(nonce);
            aes.Encrypt(nonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag,
                AssociatedData(index, true));
            await WriteIntAsync(destination, -1, cancellation);
            await destination.WriteAsync(nonce, cancellation);
            await destination.WriteAsync(tag, cancellation);
            await destination.FlushAsync(cancellation);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static async Task VerifyAsync(Stream source, string password,
        CancellationToken cancellation = default)
    {
        ValidatePassphrase(password);
        var header = new byte[Header.Length];
        await source.ReadExactlyAsync(header, cancellation);
        if (!header.AsSpan().SequenceEqual(Header))
            throw new InvalidDataException("Invalid backup header.");
        var salt = new byte[16];
        await source.ReadExactlyAsync(salt, cancellation);
        int iterations = await ReadIntAsync(source, cancellation);
        if (iterations != Iterations)
            throw new InvalidDataException("Unsupported backup format version.");
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations,
            HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            byte[] encrypted = new byte[ChunkSize];
            byte[] plain = new byte[ChunkSize];
            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            long index = 0;
            while (true)
            {
                int size = await ReadIntAsync(source, cancellation);
                if (size < -1 || size > ChunkSize)
                    throw new InvalidDataException("Invalid backup chunk size.");
                await source.ReadExactlyAsync(nonce, cancellation);
                await source.ReadExactlyAsync(tag, cancellation);
                if (size == -1)
                {
                    aes.Decrypt(nonce, ReadOnlySpan<byte>.Empty, tag,
                        Span<byte>.Empty, AssociatedData(index, true));
                    if (await source.ReadAsync(encrypted.AsMemory(0, 1), cancellation) != 0)
                        throw new InvalidDataException("Unexpected trailing backup bytes.");
                    return;
                }
                await source.ReadExactlyAsync(encrypted.AsMemory(0, size), cancellation);
                aes.Decrypt(nonce, encrypted.AsSpan(0, size), tag,
                    plain.AsSpan(0, size), AssociatedData(index, false));
                index++;
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] AssociatedData(long index, bool isFinal)
    {
        byte[] data = new byte[9];
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(0, 8), index);
        data[8] = isFinal ? (byte)1 : (byte)0;
        return data;
    }

    private static void ValidatePassphrase(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 16)
            throw new ArgumentException("Backup passphrase requires at least 16 characters.");
    }

    private static async Task WriteIntAsync(Stream stream, int value, CancellationToken cancellation)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        await stream.WriteAsync(bytes, cancellation);
    }

    private static async Task<int> ReadIntAsync(Stream stream, CancellationToken cancellation)
    {
        byte[] bytes = new byte[4];
        await stream.ReadExactlyAsync(bytes, cancellation);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }
}
