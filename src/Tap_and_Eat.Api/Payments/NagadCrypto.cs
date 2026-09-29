using System.Security.Cryptography;
using System.Text;

namespace TapAndEat.Api.Payments;

/// <summary>
/// Nagad's payment API wraps every sensitive payload in RSA: the request body
/// is encrypted with Nagad's public key (PKCS#1 v1.5) and signed with the
/// merchant's private key (SHA-256, PKCS#1 v1.5); responses come back
/// encrypted to the merchant's public key.
/// </summary>
public static class NagadCrypto
{
    public static string Encrypt(string plainText, string pgPublicKeyBase64)
    {
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(pgPublicKeyBase64), out _);
        return Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(plainText), RSAEncryptionPadding.Pkcs1));
    }

    public static string Decrypt(string cipherTextBase64, string merchantPrivateKeyBase64)
    {
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(merchantPrivateKeyBase64), out _);
        return Encoding.UTF8.GetString(rsa.Decrypt(Convert.FromBase64String(cipherTextBase64), RSAEncryptionPadding.Pkcs1));
    }

    public static string Sign(string plainText, string merchantPrivateKeyBase64)
    {
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(merchantPrivateKeyBase64), out _);
        return Convert.ToBase64String(
            rsa.SignData(Encoding.UTF8.GetBytes(plainText), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    public static bool Verify(string plainText, string signatureBase64, string publicKeyBase64)
    {
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
        return rsa.VerifyData(
            Encoding.UTF8.GetBytes(plainText), Convert.FromBase64String(signatureBase64),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
