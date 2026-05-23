namespace CargoFit;

// Edit these two constants before publishing a client build.
// Generate the public key with: `license-admin init-keys` on the server.
internal static class LicenseConfig
{
    // Base URL of the activation server (no trailing slash).
    internal const string ServerUrl = "https://license-sappe.pskwr.com/";              

    // Ed25519 public key (base64). Replace with the value printed by `license-admin init-keys`.
    internal const string ServerPublicKeyBase64 = "/juJs8OqEr9UYmme5F8Ptxkf3mCJS9e94VIr9S5jNV8=";
}
