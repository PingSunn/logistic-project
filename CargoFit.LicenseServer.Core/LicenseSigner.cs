using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace CargoFit.LicenseServer.Core;

public static class LicenseSigner
{
    public sealed record KeyPair(string PublicKeyBase64, string PrivateKeyBase64);

    public static KeyPair GenerateKeyPair()
    {
        var random = new SecureRandom();
        var privateKey = new Ed25519PrivateKeyParameters(random);
        var publicKey = privateKey.GeneratePublicKey();
        return new KeyPair(
            Convert.ToBase64String(publicKey.GetEncoded()),
            Convert.ToBase64String(privateKey.GetEncoded())
        );
    }

    public static string Sign(string privateKeyBase64, ReadOnlySpan<byte> message)
    {
        var keyBytes = Convert.FromBase64String(privateKeyBase64);
        var privateKey = new Ed25519PrivateKeyParameters(keyBytes, 0);
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(message.ToArray(), 0, message.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }

    public static bool Verify(string publicKeyBase64, ReadOnlySpan<byte> message, string signatureBase64)
    {
        try
        {
            var keyBytes = Convert.FromBase64String(publicKeyBase64);
            var publicKey = new Ed25519PublicKeyParameters(keyBytes, 0);
            var sigBytes = Convert.FromBase64String(signatureBase64);
            var verifier = new Ed25519Signer();
            verifier.Init(false, publicKey);
            verifier.BlockUpdate(message.ToArray(), 0, message.Length);
            return verifier.VerifySignature(sigBytes);
        }
        catch
        {
            return false;
        }
    }
}
