# SpacePilot — Security & Privacy Architecture

This document describes the security controls, data protection mechanisms, and architectural safeguards implemented in **SpacePilot**.

---

## 1. Credential Security & Protection at Rest

SpacePilot interacts with cloud object storage services using sensitive API keys (Access Key ID and Secret Access Key). Protecting these secrets is a fundamental architectural requirement.

### 1.1 Windows Data Protection API (DPAPI)
- **Mechanism**: All Secret Access Keys are encrypted at rest using the Windows DPAPI (`System.Security.Cryptography.ProtectedData.Protect` with `DataProtectionScope.CurrentUser`).
- **Cryptographic Binding**: The encrypted ciphertext is bound to the current Windows user profile and machine SID. It cannot be decrypted by unauthorized users on the same machine or if the SQLite database is copied to another machine.
- **No Plaintext JSON**: Non-secret configuration (endpoints, bucket names, compression parameters) is stored in standard application settings, but secrets are never stored in plaintext JSON or config files.

### 1.2 Memory Protection
- Credentials are held in memory only for the duration required to initialize the `AmazonS3Client`.
- In-memory credential objects are isolated and never exposed through public ViewModel properties or data bindings.

---

## 2. Redaction & Structured Logging Safeguards

To prevent accidental credential leaks into diagnostic logs or telemetry:
- **Redaction Service**: The `DiagnosticService` and Serilog logging sinks scan diagnostic payloads and redact all known access keys, secret tokens, and AWS Signature Version 4 Authorization headers.
- **Sanitized Diagnostics**: Exported diagnostic reports (`spacepilot_diagnostic_*.json`) mask secret keys (`***REDACTED***`) and truncate access key IDs to safe non-identifying prefixes.
- **No File Content Logging**: The logging system records only file metadata (file size, extension, processing duration, compression ratio, status codes). Raw image or PDF byte streams are never logged.

---

## 3. Network Security & Transport Layer

- **Enforced HTTPS**: All communication with S3 endpoints (Amazon S3 and DigitalOcean Spaces) defaults to secure HTTPS using modern TLS 1.2 / TLS 1.3 encryption.
- **Signature Version 4 (SigV4)**: Requests are authenticated using AWS SigV4 request signing. Signatures are calculated using HMAC-SHA256, ensuring request integrity and preventing replay attacks.
- **No Publicizing of Private Objects**: The application never alters bucket policies or object ACLs to `public-read` to enable previews. All previews are generated locally from authenticated downloads.

---

## 4. Path Traversal & Input Sanitization

Cloud object storage uses virtual object keys that may contain characters (such as `/` or `\`) that have special meaning on Windows filesystems.

- **Normalization**: All S3 keys and local file paths are normalized through `PathUtilities.NormalizeS3Prefix` and `PathUtilities.CombinePrefix`.
- **Traversal Prevention**: Relative traversal sequences (`..`, `./`) are stripped before mapping S3 keys to local temporary files.
- **Safe Temp Directories**: Downloaded temporary files are created with randomized GUID names inside a dedicated, isolated application temp directory (`%LOCALAPPDATA%\SpacePilot\temp\`) and are immediately cleaned up upon processing completion or cancellation.

---

## 5. Destructive Operation Safeguards

Accidental deletion of cloud data can lead to catastrophic data loss. SpacePilot employs strict multi-tier safeguards:

### 5.1 Explicit Confirmation Gate
- Simple file deletions require standard user confirmation.
- **Bulk Folder Prefix Deletions**: Deleting a folder prefix or recursive subtree requires the user to **manually type the target bucket name** in a confirmation dialog. If the typed text does not match the active bucket, the deletion command cannot execute.

### 5.2 Trash Prefix Soft-Delete
- When enabled in settings, deleting an object does not immediately purge it. Instead, the object is copied to a designated trash prefix (`__spacepilot_trash/<timestamp>/<key>`) before the source key is removed, allowing recovery.

### 5.3 Backup Before Replace
- When performing in-place compression on S3 objects, SpacePilot can automatically create a remote backup copy (`__spacepilot_backup/<key>`) prior to overwriting the original object.

---

## 6. Release Build Protection & Obfuscation

For production builds:
- Debug symbols (`.pdb`) are excluded from release distribution packages.
- SpacePilot application assemblies can be processed using **Obfuscar** to rename internal classes, fields, and control-flow graphs, increasing the difficulty of reverse engineering and intellectual property extraction.
- Authenticode code signing is supported via the publishing script when a valid code-signing certificate is installed.
