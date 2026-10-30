# Change summary: fix uninstallable Android APK

## Cause

Releases v1.0.62 and v1.0.63 shipped the unsigned APK. `dotnet publish` writes both `com.myadventure.app.apk` (unsigned) and `com.myadventure.app-Signed.apk`. The old rename step ran `find -name "*-Signed.apk" -o -name "*.apk" | head -1`; `-o` matches both, and `find` order follows directory hash order, so the pick was random per runner. Android rejects unsigned APKs with "package appears to be invalid". v1.0.60 is signed; v1.0.62 and v1.0.63 have no signature.

## Fix

- `build-android` requires exactly one `*-Signed.apk` and fails otherwise.
- New `Verify APK` step: `apksigner verify`, plus `aapt2 dump badging` must report `com.myadventure.app` and `versionCode` equal to the run number.
- "Unsigned" build step renamed "Debug key"; without secrets the APK is debug-signed, not unsigned.
- README and `docs/KEYSTORE.md` updated.

## Apply

Replace the changed files, push, then install the next release. Optionally delete releases v1.0.62 and v1.0.63 so Obtainium never offers them.
