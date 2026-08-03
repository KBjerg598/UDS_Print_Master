# UISI PrintMaster

UISI PrintMaster is a Windows desktop application for batch printing, PDF conversion, and document scanning from one WPF workspace.

## Features

- Mixed print queues for PDF, DOCX, RTF, TXT, and other text-based files.
- Shared print settings for copies, orientation, duplex mode, color, and page ranges.
- Built-in PDF preview and direct multi-page PDF printing.
- DOCX and RTF rendering through Microsoft Word for layout fidelity.
- PDF conversion with an independent save destination.
- WIA flatbed and automatic document feeder scanning with PDF export.
- In-app updates through signed release metadata from GitHub Releases.

## Installation

Download `UISI.PrintMaster-stable-Setup.exe` from the latest GitHub Release. The installer includes the required .NET runtime and creates Start menu and desktop shortcuts.

Microsoft Word is required for exact DOCX and RTF rendering and printing. PDF, text printing, conversion, and compatible WIA scanning do not require Word.

## Development

Open `UDS_Print_Master.slnx` in Visual Studio or build it with the .NET 10 SDK:

```powershell
dotnet build .\UDS_Print_Master.slnx
```

Release packaging and update publishing are documented in [RELEASING.md](RELEASING.md).
