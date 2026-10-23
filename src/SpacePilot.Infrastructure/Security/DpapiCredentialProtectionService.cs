using System.Security.Cryptography;
using System.Text;
using SpacePilot.Core.Interfaces;

namespace SpacePilot.Infrastructure.Security;

public class DpapiCredentialProtectionService : ICredentialProtectionService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SpacePilot.S3.ProtectedEntropy.2026");

    public string ProtectSecret(string plainSecret)
    {
        if (string.IsNullOrEmpty(plainSecret))
            return string.Empty;

        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainSecret);
            byte[] cipherBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipherBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to encrypt secret using Windows DPAPI.", ex);
        }
    }

    public string UnprotectSecret(string encryptedSecret)
    {
        if (string.IsNullOrEmpty(encryptedSecret))
            return string.Empty;

        try
        {
            byte[] cipherBytes = Convert.FromBase64String(encryptedSecret);
            byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception)
        {
            // If already plaintext or cannot decrypt on this machine
            return encryptedSecret;
        }
    }
}
