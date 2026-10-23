# SpacePilot — User Guide

Welcome to **SpacePilot — S3 Storage Explorer & Smart Compressor**.

**Tagline:** *Explore. Optimize. Reclaim space.*

SpacePilot combines the speed and responsiveness of Windows File Explorer with a high-capacity S3/DigitalOcean Spaces storage analyzer and a bulk image/PDF compression studio.

---

## 1. Getting Started

### System Requirements
- Windows 10 (version 1809+) or Windows 11 (x64 architecture).
- No separate .NET runtime required when running the self-contained executable.

### First Launch
1. Double-click `SpacePilot.exe`.
2. The Dashboard displays storage metrics, recent jobs, and quick-action shortcuts.
3. Switch between **Light Theme** and **Dark Theme** anytime using the theme toggle in the header or via `Settings`.

---

## 2. Connecting to S3 / DigitalOcean Spaces

SpacePilot supports any S3-compatible object storage provider, with first-class presets for **DigitalOcean Spaces** and **Amazon S3**.

### Adding a Connection Profile
1. Navigate to **Settings** -> **Storage Profiles** or click **Connect to Storage** from the top menu.
2. Click **Add Profile**.
3. Fill in the connection parameters:
   - **Profile Name**: E.g., `My DO Space` or `AWS Production`.
   - **Provider Type**: Choose `DigitalOceanSpaces` or `AmazonS3`.
   - **Service Endpoint**: E.g., `https://blr1.digitaloceanspaces.com` (DO Spaces) or `https://s3.us-east-1.amazonaws.com` (AWS).
   - **Region**: E.g., `blr1`, `nyc3`, `us-east-1`.
   - **Access Key**: Your S3 API Key.
   - **Secret Key**: Your S3 Secret Key (*automatically encrypted via Windows DPAPI upon save*).
   - **Bucket Name**: E.g., `docs456`.
4. Click **Test Connection** to verify endpoint availability and credential validity.
5. Click **Save Profile**.

---

## 3. Storage Explorer

The **Storage Explorer** provides a visual, File Explorer-style navigation experience for massive buckets containing up to millions of objects.

### Browsing and Navigation
- **Virtual Folders**: S3 keys with `/` delimiters are automatically represented as familiar hierarchical folders.
- **Breadcrumb Bar**: Shows the current directory prefix. Click any segment in the breadcrumb path to jump up the hierarchy instantly.
- **Up Button**: Ascend to the parent folder.
- **Sorting**: Click any column header (`Name`, `Size`, `Type`, `Last Modified`) to sort ascending or descending.

### Scanning & Indexing
- Click **Scan Bucket** to start or update the local catalog.
- SpacePilot streams object metadata into a local high-performance SQLite database without consuming system RAM.
- **Recursive Folder Sizes**: Once scanned, every folder displays its true recursive size, file count, and subtree footprint.
- **Search & Filters**: Search within the bucket by keyword, extension (`.jpg`, `.png`, `.pdf`), or minimum size.

---

## 4. Bulk Media Compression Studio

SpacePilot includes a high-performance bulk compression engine powered by Magick.NET and PdfSharpCore.

### Adding Files
- Click **Add Files** or **Select Folder** to load files.
- Drag and drop images or PDFs directly into the workspace.

### Compression Profiles
Choose between three carefully engineered quality modes:
1. **Lossless First (Default)**:
   - Removes redundant metadata and optimizes compression tables.
   - Preserves exact pixel dimensions and visual fidelity.
   - Preserves PDF vector content, fonts, and searchable text.
2. **Visually Optimized**:
   - Re-encodes images with high-quality perceptual compression (JPEG/WebP quality 80+).
   - Applies the 15% minimum savings threshold before saving.
3. **Maximum Savings (Manual Opt-In)**:
   - Enables downscaling, format conversion (e.g., JPEG/PNG to WebP), and aggressive stream optimization.

### Resize Controls
- **Fit to Dimensions**: Set maximum width and height while preserving aspect ratio.
- **Resize by Percentage**: Scale images by 10% – 100%.
- **Longest Edge**: Constrain the maximum dimension of high-resolution camera photos.

### Side-by-Side Comparison
- Select any processed file and click **Compare / Test** to open the interactive side-by-side inspection view.
- Pan and zoom synchronized viewports to verify crispness, color fidelity, and text readability.

---

## 5. Local Folder Compression Workflow

SpacePilot provides a dedicated **Local Files** module that safely optimizes folders on your local drives without overwriting originals.

### Sibling `compressed/` Convention
When processing a directory, SpacePilot writes output files into a sibling subfolder named `compressed` inside each source directory:

```text
D:\Documents\invoice.jpg          --> D:\Documents\compressed\invoice.jpg
D:\Documents\Reports\annual.pdf   --> D:\Documents\Reports\compressed\annual.pdf
```

### Safety Rules
- **Non-destructive**: Source files remain completely untouched.
- **Loop Prevention**: Existing `compressed/` subfolders are automatically detected and excluded from recursive scans.
- **Collision Detection**: If an output already exists, you are prompted to skip or replace.

---

## 6. Remote S3 Compression Workflow

SpacePilot can compress files directly inside remote S3 buckets with complete peace of mind.

### Execution Modes
1. **Dry Run (Preview)**:
   - Downloads eligible files to temporary storage, runs compression, and calculates exact potential savings without touching the S3 bucket.
2. **Write to Staging Prefix**:
   - Uploads compressed results to a staging prefix (e.g., `__spacepilot_compressed/`) in the same bucket so you can review results before replacing.
3. **Replace Original Object**:
   - Replaces the original S3 object only if:
     - The file achieves at least the minimum savings threshold (15% default).
     - The compressed file passes integrity and non-empty validation.
     - An automatic backup copy is created first (if enabled in settings).
   - Preserves all remote metadata (ContentType, Cache-Control, Custom User Metadata).

---

## 7. Storage Cleanup & Deletion Safeguards

### Reports & Diagnostics
- **Largest Files**: Instant view of the largest objects in the bucket.
- **Largest Folders**: Rollup analysis of the heaviest prefixes.
- **Duplicate Candidates**: Detects matching file sizes and names across different folders.

### Deletion Safety
- Deleting files or folder prefixes requires explicit authorization.
- For bulk folder deletion, SpacePilot requires you to **type the exact bucket name** into a confirmation dialog before proceeding.
- Optional **Trash Prefix**: Objects can be moved to a designated `__spacepilot_trash/` prefix for later recovery.

---

## 8. Logs & Troubleshooting

- **Live Activity Grid**: Monitor real-time progress, processed bytes, compression ratios, and status codes.
- **Export Diagnostic Report**: Found under **Tools** -> **Diagnostic Report**. Creates a comprehensive diagnostic package with all credentials and sensitive information automatically redacted.
- **Logs Directory**: Open via **File** -> **Open Logs Folder** to inspect rolling Serilog logs.
