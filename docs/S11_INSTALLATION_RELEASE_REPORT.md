# Data-M S11 Windows Installation / Release Report

**Status: candidate build pending S11 Windows machine CI; no official v1.0 release.**

- Initial release format: Tauri NSIS current-user installer, unsigned, Windows x64 Runner; portable diagnostic EXE retained. Application product version 0.1.0; candidate manifest version 0.1.0-s11-candidate.
- Build and verification: npm run tauri:build then scripts/s11-release-manifest.ps1. The script requires exactly one NSIS installer, independently copies portable EXE and installer, computes SHA-256 for each, emits release-manifest.json and SHA256SUMS.txt, and verifies the hashes against those real files. The GitHub Actions upload is a ZIP container; its ZIP hash is not either binary hash.
- Manifest fields: releaseVersion, sourceSha, schemaSupported=[1,2,3], schemaLatest=3, buildRunUrl, status=machine-candidate-only, signatureStatus=unsigned, and per-binary file name/hash/size.
- SQLite migration: strict v1→v2→v3 upgrade transaction guards; no automatic downgrade. S7 snapshots and isolated restore are supported but require real Windows verification.
- Data retention: code points storage to Tauri app_local_data_dir separate from installed binary. No application-uninstall cleanup implemented; nonetheless actual upgrade/uninstall retention on clean Windows must be verified.
- Pending: successful exact-SHA Windows CI, installer & EXE Artifact IDs and real SHA-256, first launch after NSIS install, upgrade interruption, rollback/no downgrade, uninstall, Windows 11 100%/150% DPI, WebView2 dialogues, real folder drag drop, S7 external backup and S10 Figure export.
- Human acceptance: manual_pending; stage cannot be marked accepted or released as v1.0.
