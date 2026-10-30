# Android Keystore Setup

## Generate a keystore (one time)

```bash
keytool -genkey -v -keystore android.keystore \
  -alias myalias -keyalg RSA -keysize 2048 -validity 10000 \
  -storepass YOUR_PASSWORD -keypass YOUR_PASSWORD \
  -dname "CN=MyAdventure, OU=Dev, O=MyAdventure, L=City, ST=State, C=US"
```

Keep this keystore forever. Android refuses updates signed by a different key.

## Add to GitHub Secrets

1. Base64 encode the keystore:
```bash
base64 -w 0 android.keystore > android.keystore.base64
```

2. In your GitHub repo, go to **Settings → Secrets and variables → Actions** and add:
   - `ANDROID_KEYSTORE_BASE64` — contents of `android.keystore.base64`
   - `ANDROID_SIGNING_PASSWORD` — the password you used above

Without these secrets CI signs with the SDK debug key. That APK installs, but cannot update a release-signed install.

## Local builds

`dotnet publish -c Release` writes two APKs to the output folder:

| File | Installable |
|------|-------------|
| `com.myadventure.app-Signed.apk` | yes |
| `com.myadventure.app.apk` | no — unsigned, Android reports "package appears to be invalid" |

Without signing properties the `-Signed.apk` uses the debug key. For a release-signed local build, place `android.keystore` in the repo root (it's gitignored) and pass:

```bash
dotnet publish src/MyAdventure.Android/MyAdventure.Android.csproj -c Release --output ./publish/android \
  -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=$PWD/android.keystore \
  -p:AndroidSigningStorePass=YOUR_PASSWORD -p:AndroidSigningKeyPass=YOUR_PASSWORD \
  -p:AndroidSigningKeyAlias=myalias
```

## Verify an APK

```bash
apksigner verify --verbose --print-certs MyAdventure-android-N.apk
aapt2 dump badging MyAdventure-android-N.apk | grep '^package:'
```

Both tools live in `$ANDROID_HOME/build-tools/<version>/`. CI runs the same checks before upload.
