# Runbook: verifying the MAUI app on devices (XD01-461)

The MAUI work of ADR-002 (XD01-423, 438, 463, 464, 465, 460) and XD01-476 was unit-tested and built, but never run on
a device. This runbook is the one checklist for that run: setup, the steps with their exact expected results, and a
results table per platform to fill in and paste on XD01-461. MapLibre mode has its own checklist, **XD01-460 A–G**
(the resolution comment on [XD01-460](https://xdicor.atlassian.net/browse/XD01-460)); run it after this one, on the
same builds, and record it there.

## What was verified without a device (2026-11-27, Mac, Xcode 27.2, .NET 10 MAUI workloads 10.0.20)

| Check | Result |
|---|---|
| `dotnet build apps/GeoAssets.MAUI -f net10.0-android` | 0 warnings, 0 errors; produces `com.geoassets.app-Signed.apk` (`libe_sqlcipher.so` for arm64-v8a and x86_64; `MainActivity` is the launcher in the merged manifest) |
| `dotnet build apps/GeoAssets.MAUI -f net10.0-ios` | 0 warnings, 0 errors; produces `iossimulator-arm64/GeoAssets.MAUI.app` (not run: this Mac has no simulator runtime) |
| `dotnet build apps/GeoAssets.MAUI -f net10.0-maccatalyst` | 0 warnings, 0 errors; produces `maccatalyst-arm64/GeoAssets.app` (ad-hoc signed) |
| Mac Catalyst launch | The process starts, adopts the UIScene life cycle and stays up (40 s observed, no managed exception in its console or unified log). Its window could not be seen: the session had no WindowServer access (`screencapture`: *could not create image from display*), so no UI was driven or looked at. |
| SQLCipher on the Mac Catalyst runtime (temporary probe in the launched app, not shipped) | develop's order: provider `e_sqlite3`, no `cipher_version`, file header `SQLite format 3` (plaintext). This PR's order: provider `e_sqlcipher`, `cipher_version` 4.5.2 community, header random; a plaintext file encrypted in place, its row read back with the key. |
| SecureStorage on Mac Catalyst, ad-hoc signed | `Error adding record: MissingEntitlement`: the offline database can't get its key. With a `keychain-access-groups` entitlement the ad-hoc app is killed at launch (exit 137). A provisioning profile for `com.geoassets.app` is needed (see Setup). |
| `tests/GeoAssets.MAUI.Tests` | the SQLCipher provider, the plaintext-to-encrypted upgrade, and the pre-XD01-438 tile table migrated on an existing database with data (`SqlCipherDatabaseTests`) |

Found and fixed by XD01-461 (so every step below starts from this PR or later):

- **The app could not be installed anywhere.** `GeoAssets.MAUI` built a library: no `OutputType=Exe`, no Android
  `MainApplication`/`MainActivity`, no iOS/Mac Catalyst `Main`. It now builds an `.apk`/`.app` (Windows stays a library).
- **iOS/Mac Catalyst apps built with the 27 SDKs don't launch without the UIScene life cycle** (*"UIScene life cycle is
  required for apps built with this SDK"*, observed). Both now declare a scene manifest and `SceneDelegate`; the iOS
  sign-in callback is also taken from the scene (`SceneOpenUrl`).
- **The SQLCipher database was written in plaintext.** sqlite-net-pcl 1.11 replaced the SQLCipher provider with plain
  SQLite, which ignores the key. `SqlCipherDatabase` now selects SQLCipher after it, encrypts a plaintext file in place
  on first open, and refuses a connection that isn't SQLCipher's.
- `App`'s constructor was `internal`, which MAUI's container can't call (it would have failed at start-up).

## Setup (every platform)

1. **Build from develop at or after the XD01-461 merge.** No earlier commit produces an installable app, so there is no
   older install in the field: the *upgrade* steps below use an install of this build and a hand-made old database.
2. **`apps/GeoAssets.MAUI/appsettings.json`** (embedded: rebuild after editing):
   - `GeoAssetsServer:BaseUrl` and `ServiceOrders:ApiBaseUrl`: an **https** gateway the device can reach (`localhost` is
     the device itself; Android blocks cleartext and iOS ATS blocks `http`).
   - `AzureAdCiamMaui:Authority`/`ClientId`: real values (the map page needs a signed-in user). On Android also set the
     `msal<ClientId>` scheme in `Platforms/Android/AndroidManifest.xml`.
   - For step 8 only: `OfflinePacks:LargePackWarningMegabytes` = `1` (any pack over 1 MB then counts as large).
3. **Signing.**
   - Android: the debug keystore is created automatically.
   - iOS device and **iOS simulator**: SecureStorage (which holds the offline database's key) needs a keychain access
     group. Create an `Entitlements.plist` with `keychain-access-groups` = `$(AppIdentifierPrefix)com.geoassets.app` and
     build with `-p:CodesignEntitlements=<that file> -p:CodesignKey="Apple Development: …" -p:CodesignProvision="<a
     profile for com.geoassets.app>"`. With the entitlement and no profile the build stops with *Could not find any
     available provisioning profiles* (observed); without the entitlement expect `MissingEntitlement` in the log and the
     offline steps to fail.
   - Mac Catalyst: the same, with a Mac Catalyst profile for `com.geoassets.app`. Ad-hoc builds launch but can't use
     SecureStorage (observed), so steps 4–9 need the signed build.
4. **Packs** on a computer, then on the device (Android: `adb push <file> /sdcard/Download/`; iOS simulator: drag the
   file onto the simulator → Files; iOS device: Files/AirDrop; Mac: any folder):
   - a **vector** pack with polygons and lines crossing tile seams, e.g. from the network import CLI:
     `plan --export pmtiles --min-zoom 10 --max-zoom 14`;
   - a **raster** pack (PNG, JPEG or WebP tiles);
   - for step 8, any pack over the threshold from setup 2.
5. **Logs.** Android: `adb logcat | grep -i -E "geoassets|dotnet|mono|sqlite|chromium"`. iOS simulator:
   `xcrun simctl launch --console booted com.geoassets.app`. Mac Catalyst: run
   `…/GeoAssets.app/Contents/MacOS/GeoAssets.MAUI` from a terminal, or `log stream --predicate 'process == "GeoAssets.MAUI"'`.
   Debug builds enable the Web inspector (Safari → Develop; Chrome `chrome://inspect`).
6. **Where the app keeps its data** (`FileSystem.AppDataDirectory`; the database is `geoassets-offline.db3`, the packs
   `offline-packs/<id>.pmtiles` with `offline-packs/pack-info/<id>.json`):
   - Android: `/data/data/com.geoassets.app/files/` — `adb exec-out run-as com.geoassets.app ls -la files files/offline-packs`;
   - iOS simulator: `"$(xcrun simctl get_app_container booted com.geoassets.app data)/Library/"`;
   - iOS device: the app container's `Library/` (Xcode → Devices → Download Container);
   - Mac Catalyst: `~/Library/` (observed — the app isn't sandboxed, so its files sit in the user's Library root).
7. **Is the database encrypted?** The first 16 bytes must **not** be `SQLite format 3`:
   Android `adb exec-out run-as com.geoassets.app head -c 16 files/geoassets-offline.db3 | xxd`; elsewhere
   `head -c 16 <data dir>/geoassets-offline.db3 | xxd`. The file appears the first time an offline store is used (a
   vector pack drawing is the first in these steps), not at launch.

## Steps (every platform)

| # | Step | Expected result |
|---|---|---|
| 1 | Fresh install (uninstall first), launch. | The app opens on the sign-in page (`MauiLogin`); no crash, no unhandled exception in the log. iOS/Mac Catalyst: no *UIScene life cycle is required* message. |
| 2 | Sign in. | The browser sign-in returns to the app (iOS: through the scene) and the map page opens. |
| 3 | **Map page (XD01-476).** Look at the page. | `Index` under `MainLayout`: sidebar with its nav (*Offline maps* after Collections), top bar, the Leaflet map with OpenStreetMap; panels open and close. Shared panels show their localization keys (MAUI's `NoOpJsonStringLocalizer`) — expected. |
| 4 | **Path-only pick + import (XD01-423/438).** Collections → connect → *Offline map pack (PMTiles)* → pick the vector pack. | The picker lists `.pmtiles` files (Android: `application/vnd.pmtiles`/`octet-stream`; Apple: `public.data`). It connects; the map fits to the pack and draws its points, lines and polygons. `offline-packs/<16 hex>.pmtiles` has the pack's exact size and `pack-info/<id>.json` its file name; no `.partial` left. Memory doesn't grow by the pack's size. Database: encrypted (setup 7). Note in *Notes*: Android's picker copies a content URI into `cache/` first (`adb exec-out run-as com.geoassets.app du -sh cache`) — record that size. |
| 5 | **Offline draw.** Airplane mode on, kill the app, cold-start it, connect the same pack again (MAUI keeps no pool across starts). | Starts with the cached sign-in; the pack connects without a second copy (`offline-packs` unchanged) and draws with no network. Pan and zoom: tiles keep drawing. |
| 6 | **Seams (XD01-463).** Zoom to a polygon and a line that cross tile boundaries. | One outline per polygon, no line along a tile edge; one line, no gap or overlap at the seam; panning doesn't redraw them as pieces. |
| 7 | **Raster pack (XD01-465).** Connect the raster pack, online and then in airplane mode. | Drawn as a basemap: over OpenStreetMap inside its bounds, under every feature and the vector pack; the map fits to its bounds; past its highest zoom it scales up, below its lowest nothing. Works offline. |
| 8 | **Large pack (XD01-465).** With the threshold from setup 2, connect a pack over it, then press Connect again. | First Connect: the dialog says the pack is large, its size, that importing copies it and to keep the app open; nothing new in `offline-packs`. Second Connect: imported and drawn. Connecting it once more later doesn't ask again. |
| 9 | **Offline maps panel (XD01-464).** Open *Offline maps*. Delete a pack another (not active) collection draws, then try the active collection's. | Each pack: name, kind (map features / basemap), size, date added, zooms, area, the collections drawing it; the total. Delete asks for confirmation, takes the pack off the map, removes `<id>.pmtiles` and `pack-info/<id>.json`, and the total drops. The active collection's pack can't be deleted (disabled, with a hint). |
| 10 | **Binary pick (Bytes).** | Not reachable in MAUI today: its plugins are Rest and PMTiles, and the only `Bytes` file field is the Shapefile plugin's, which MAUI doesn't compose. Record *N/A* unless that changes. |
| 11 | **Upgrade, real.** Keep the data from steps 4–9, install a newer build over it (Android `adb install -r`; iOS `simctl install`/Xcode over the app; Mac Catalyst rebuild in place), launch, sign in, connect a stored pack. | No data loss: `offline-packs` and the database are still there, the database still encrypted and opening (the pack draws), no new key prompt. |
| 12 | **Upgrade, old database.** App not running. Replace the database with a plaintext pre-XD01-438 one (below), launch, sign in, draw a vector pack. | The database is encrypted in place (setup 7; no `geoassets-offline.db3.encrypting` left), the tile table gains `ContentType`/`ContentEncoding` and accepts the pack's tiles (the pack draws). The old row can't be read back without the key on a device; `SqlCipherDatabaseTests` asserts it is kept. |
| 13 | **MapLibre mode.** Run XD01-460's checklist A–G on this build. | As listed there; record on XD01-460. |

The old database for step 12 (`CachedAtUtc` is in .NET ticks):

```bash
sqlite3 geoassets-offline.db3 '
CREATE TABLE "local_vector_tiles"("Key" varchar primary key not null, "Z" integer, "X" integer, "Y" integer, "Data" blob, "CachedAtUtc" bigint);
INSERT INTO local_vector_tiles VALUES ('"'"'5:10:12'"'"', 5, 10, 12, x'"'"'01020304'"'"', 639264402000000000);'
head -c 16 geoassets-offline.db3 | xxd   # SQLite format 3 — plaintext, as before XD01-461
```

Put it in place — Android: `adb push geoassets-offline.db3 /data/local/tmp/` then
`adb shell run-as com.geoassets.app cp /data/local/tmp/geoassets-offline.db3 files/`; iOS simulator / Mac Catalyst:
copy it into the data directory from setup 6.

## Per platform

**Android** (device or emulator, API 23+; an emulator needs a system image — none is installed on the dev Mac):
`dotnet build apps/GeoAssets.MAUI -f net10.0-android -c Debug`, then
`adb install -r apps/GeoAssets.MAUI/bin/Debug/net10.0-android/com.geoassets.app-Signed.apk` and
`adb shell monkey -p com.geoassets.app 1` (or `dotnet build … -t:Run`). `adb` is at
`~/Library/Android/sdk/platform-tools/adb`. Extra: in step 4 also pick from a cloud provider (Drive) to get a real
content URI; in step 5 also turn the device off and on.

**iOS** (device, or a simulator after `xcodebuild -downloadPlatform iOS`): simulator —
`dotnet build apps/GeoAssets.MAUI -f net10.0-ios -c Debug <signing from setup 3>`, then
`xcrun simctl install booted apps/GeoAssets.MAUI/bin/Debug/net10.0-ios/iossimulator-arm64/GeoAssets.MAUI.app` and
`xcrun simctl launch --console booted com.geoassets.app`; device — add `-p:RuntimeIdentifier=ios-arm64` and run with
`-t:Run`. Extra: in step 4 pick from iCloud Drive and from *On My iPhone* (a security-scoped URL; the picker hands the
app a temporary copy, which the import copies into `offline-packs`); in step 5 also restart the device.

**Mac Catalyst**: `dotnet build apps/GeoAssets.MAUI -f net10.0-maccatalyst -c Debug <signing from setup 3>`, then
`open apps/GeoAssets.MAUI/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/GeoAssets.app`. An ad-hoc build is enough for
steps 1–3; steps 4–12 need the signed build. Extra: use the mouse and the trackpad (right-click on a feature opens its
menu in Leaflet mode). After editing an `Info.plist`, build with `--no-incremental` (an incremental build kept the old
one, observed).

**Windows**: not shipped — the Windows TFM builds only on Windows and stays a library (no `Platforms/Windows` entry
point). Record *N/A*.

The .NET for iOS/Mac Catalyst workload on the dev Mac (26.4) predates its Xcode (27.2), so `GeoAssets.MAUI.csproj`
sets `ValidateXcodeVersion=false`; with the matching Xcode, pass `-p:ValidateXcodeVersion=true` to have it checked.

## Results

Fill one block per platform and run, then paste it on XD01-461.

| Platform | OS version | Device or simulator | Build (commit) | Pass/Fail | Notes |
|---|---|---|---|---|---|
| Android | | | | | |
| iOS | | | | | |
| Mac Catalyst | | | | | |
| Windows | — | — | — | N/A | not shipped |

Per step (copy for each platform):

| # | Step | Pass/Fail | Notes (what was seen; log lines; sizes) |
|---|---|---|---|
| 1 | Fresh install, launch | | |
| 2 | Sign in | | |
| 3 | Map page (XD01-476) | | |
| 4 | Path-only pick + import | | |
| 5 | Offline draw | | |
| 6 | Seams (XD01-463) | | |
| 7 | Raster pack (XD01-465) | | |
| 8 | Large pack (XD01-465) | | |
| 9 | Offline maps panel (XD01-464) | | |
| 10 | Binary pick (Bytes) | N/A | not reachable in MAUI's composition |
| 11 | Upgrade, real | | |
| 12 | Upgrade, old database | | |
| 13 | MapLibre A–G (XD01-460) | | recorded on XD01-460 |

A failure is fixed on XD01-461 or split out as its own Error ticket linked to it.
