using System.Security.Cryptography;
using System.Text;

namespace EntwineAgents.Tokenisation;

/// <summary>Encrypts/decrypts a real identity at rest in the key store. Abstracted so a per-tenant key (or a
/// KMS/HSM) can replace the default without touching the store.</summary>
public interface IValueProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}

/// <summary>AES-GCM authenticated encryption with a single 32-byte key (per-tenant keys are a later refinement).
/// Output is base64(nonce ‖ tag ‖ ciphertext); a fresh random nonce per call, so the same plaintext never
/// produces the same ciphertext.</summary>
public sealed class AesValueProtector : IValueProtector
{
    private readonly byte[] _key;

    public AesValueProtector(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("Key must be 32 bytes (AES-256).", nameof(key));
        _key = key;
    }

    public string Protect(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[pt.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, pt, ct, tag);
        var blob = new byte[nonce.Length + tag.Length + ct.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, nonce.Length);
        ct.CopyTo(blob, nonce.Length + tag.Length);
        return Convert.ToBase64String(blob);
    }

    public string Unprotect(string protectedValue)
    {
        var blob = Convert.FromBase64String(protectedValue);
        var nonce = blob.AsSpan(0, 12);
        var tag = blob.AsSpan(12, 16);
        var ct = blob.AsSpan(28);
        var pt = new byte[ct.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }
}
