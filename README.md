# SpacePilot — S3 Storage Explorer & Smart Compressor

> **Tagline:** *Explore. Optimize. Reclaim space.*

[![Platform: Windows x64](https://img.shields.io/badge/platform-Windows%20x64-blue.svg)](https://microsoft.com/windows)
[![.NET 9.0 LTS](https://img.shields.io/badge/.NET-9.0%20LTS-purple.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](docs/ThirdPartyLicenses.md)
[![Tests: 29 Passing](https://img.shields.io/badge/tests-29%20passed-brightgreen.svg)](tests/)

**SpacePilot** is an enterprise-grade Windows desktop application that combines the responsiveness of Windows File Explorer, the analytical power of a cloud storage tree analyzer, and a media compression studio for high-volume image and PDF optimization.

---

## 🌟 Key Features

### 1. Cloud Object Storage Explorer (S3 & DigitalOcean Spaces)
- **High-Scale Virtual Folder Browsing**: Browse millions of remote S3 objects visually with breadcrumbs and fast pagination.
- **Accurate Recursive Folder Sizes**: Aggregates true recursive size, total object count, and percentage of bucket storage using local SQLite indexing.
- **DigitalOcean Spaces First-Class Support**: Configured for regional endpoints (e.g., `blr1.digitaloceanspaces.com`, `nyc3.digitaloceanspaces.com`) alongside Amazon S3.
- **Search & Multi-Criteria Filtering**: Filter by extension, minimum size, date ranges, and keyword.

### 2. Bulk Media Compression Studio
- **Image Formats**: JPEG, PNG, WebP, BMP, TIFF powered by `Magick.NET` (ImageMagick).
- **PDF Optimization**: Lossless structural stream compression and object tree compaction powered by `PdfSharpCore` (MIT licensed).
- **Three Quality Modes**:
  - **Lossless First (Default)**: Compresses tables, strips nonessential metadata, and preserves 100% visual pixel fidelity.
  - **Visually Optimized**: High-efficiency re-encoding with configurable quality sliders (80-90%).
  - **Maximum Savings**: Format conversion (to WebP), smart downscaling, and aggressive compression.
- **Side-by-Side Visual Inspection**: Zoom and pan before/after preview with exact byte savings and percentage indicators.
- **Pre-flight Threshold Filtering**: Automatically skips small files (<200 KB image, <300 KB PDF) and rejects operations that fail to achieve the required minimum savings (15% default).

### 3. Local Folder Compression Workflow
- **Sibling `compressed/` Convention**: Automatically creates a sibling `compressed/` subfolder in each file's parent folder (`D:\Docs\invoice.jpg` ➔ `D:\Docs\compressed\invoice.jpg`).
- **Directory Isolation**: Strictly isolates and excludes existing `compressed/` folders to prevent recursive loops.
- **Collision Safety**: Prompts the user before overwriting existing output files.

### 4. Remote S3 Compression Pipeline
- **Dry Run**: Downloads and compresses files in memory/temp to preview projected savings without touching remote storage.
- **Staging Prefix**: Uploads compressed files to a safe staging prefix (e.g., `__spacepilot_compressed/`) for user review before replacement.
- **Replace Original**: In-place replacement with automatic remote backup creation (`__spacepilot_backup/`) and full remote metadata preservation (ContentType, Cache-Control, custom headers).

### 5. Storage Cleanup & Deletion Safety
- **Reports**: Discover the largest files, largest folders, and duplicate candidates across the entire bucket.
- **Multi-Tier Safeguards**: Destructive folder deletion requires **typing the exact bucket name** into a confirmation dialog.
- **Trash Prefix**: Soft-delete workflow moving objects to `__spacepilot_trash/` for recoverable cleanup.

### 6. Persistence, Security & Architecture
- **Windows DPAPI Encryption**: S3 secret keys are encrypted at rest using Windows DPAPI and never stored in plaintext JSON.
- **SQLite WAL Mode**: High-concurrency streaming inserts with batching (500 items/commit) for bucket catalogs.
- **Resilient Job Engine**: Bounded concurrency (`SemaphoreSlim`), pause, resume, and cancellation support.
- **Modern Fluent UI**: Dark and Light theme toggle, responsive layout, custom styled DataGrids, and real-time activity stream.

---

## 🏛 Solution Architecture

```text
SpacePilot/
├── SpacePilot.sln
├── src/
│   ├── SpacePilot.Core/           # Domain models, enums, interfaces, utilities
│   ├── SpacePilot.Infrastructure/ # S3, SQLite, Magick.NET, PdfSharpCore, DPAPI, Serilog
│   ├── SpacePilot.Application/    # Job manager, local folder service, compression orchestrator
│   └── SpacePilot.App/            # WPF desktop application, MVVM ViewModels, Fluent styles
├── tests/
│   └── SpacePilot.UnitTests/      # xUnit automated unit & integration test suite
├── scripts/
│   ├── build.ps1                  # Build script
│   ├── test.ps1                   # Automated test runner
│   └── publish.ps1                # Self-contained Win-x64 publishing script
├── config/
│   └── spacepilot.sample.json     # Non-secret configuration sample
├── docs/
│   ├── Architecture.md            # System design & architecture details
│   ├── UserGuide.md               # Complete operational user guide
│   ├── Security.md                # DPAPI encryption & data protection model
│   └── ThirdPartyLicenses.md      # Dependency attributions and MIT/Apache-2.0 licenses
└── publish/
    └── SpacePilot.exe             # Self-contained Windows x64 executable (~175 MB)
```

---

## 🚀 Quick Start & Building

### Prerequisites
- Windows 10/11 x64
- [.NET 9.0 SDK](https://dotnet.microsoft.com/)

### 1. Build the Solution
Run the build script:
```powershell
.\scripts\build.ps1 -Configuration Release
```

### 2. Run Automated Tests
Execute the full test suite (29 tests):
```powershell
.\scripts\test.ps1
```

### 3. Publish Self-Contained Executable
Publish `SpacePilot.exe` as a single-file, self-contained Windows x64 binary:
```powershell
.\scripts\publish.ps1
```
The output executable will be created at `publish\SpacePilot.exe`. It can be run on any Windows x64 machine without needing a separate .NET runtime installation.

---

## 🧪 Test Results

The automated test suite in `tests/SpacePilot.UnitTests/` validates all core subsystems:
- **Image Compression**: JPEG, PNG, transparency preservation, resize modes, threshold filtering.
- **PDF Optimization**: Lossless stream optimization, page count invariance, corruption detection.
- **Storage Indexer**: Recursive folder rollup calculations, batch streaming inserts, duplicate detection.
- **Local Workflow**: Sibling `compressed/` output generation, directory exclusion.
- **Security & Persistence**: DPAPI encrypt/decrypt, SQLite settings and job persistence.
- **S3 Service**: Endpoint configuration, pagination, and retry logic.

```text
Passed!  - Failed: 0, Passed: 29, Skipped: 0, Total: 29, Duration: 7.8 s
```

---

## 📄 Documentation

- [Architecture & Design Details](docs/Architecture.md)
- [Comprehensive User Guide](docs/UserGuide.md)
- [Security & DPAPI Credentials](docs/Security.md)
- [Third-Party Licenses](docs/ThirdPartyLicenses.md)

---

## ⚖️ License

SpacePilot is licensed under the [MIT License](LICENSE).
Third-party dependencies are licensed under their respective MIT and Apache 2.0 licenses.
