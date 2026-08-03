# Releasing UISI PrintMaster

UISI PrintMaster uses Velopack for its Windows installer and in-app updates. The application reads stable releases from the public GitHub repository and does not embed an access token.

## Local package

```powershell
.\scripts\Build-Release.ps1 -Version 0.1.0 -Channel stable
```

The script restores the pinned Velopack CLI, publishes a self-contained Windows build, and writes the installer and update packages to `artifacts/releases`.

## GitHub release

Run the **Publish Windows release** workflow and provide a semantic version without the `v` prefix. The workflow builds the installer and uploads the complete Velopack release feed to GitHub Releases.

Before publishing a version:

1. Update `RELEASE_NOTES.md` in English.
2. Set the project version and create the matching annotated Git tag.
3. Run a local Release build and smoke test.
