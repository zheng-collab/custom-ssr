# Keeping antivirus from quarantining SecureGateway

SecureGateway bundles `v2ray.exe`, a legitimate proxy engine that some antivirus products flag
on a heuristic ("not-a-virus / HackTool / Proxy"). Unsigned binaries make this worse. This is a
false positive on our own app. For a small internal fleet (~10 machines) and a ~$0 budget we fix
it without a paid code-signing certificate, using four layers:

## 1. Folder install, not a self-extracting exe  (done in the build)
The Windows app is published as a normal folder of files and installed by the Inno Setup
installer — **not** a single-file self-extracting `.exe`. A self-extracting bundle unpacks native
DLLs and the engine to `%TEMP%` on every launch, which looks like a dropper to AV heuristics.
Nothing to do; this is how `build.ps1` now publishes.

## 2. Sign the binaries with our own self-signed certificate  (~$0)
A public cert is $200–300/yr. For internal use a self-signed cert is enough **if the fleet trusts
it**. One-time, on the build machine:

```powershell
.\scripts\make-selfsigned-cert.ps1            # creates certs\SecureGateway-CodeSign.pfx (+ .cer)
.\scripts\build.ps1 -Installer -Zip -SignCert certs\SecureGateway-CodeSign.pfx
```

Then trust the public cert once per PC (as admin), or push it via Active Directory GPO:

```powershell
.\scripts\trust-cert.ps1 -CerPath SecureGateway-CodeSign.cer
```

GPO path for many machines at once: *Computer Configuration → Policies → Windows Settings →
Security Settings → Public Key Policies → Trusted Publishers* (and *Trusted Root Certification
Authorities*). Keep the `.pfx` secret — it is the signing key and is git-ignored.

## 3. Defender exclusion at install time  (done in the installer)
The installer offers a checked task **"Add this folder to Windows Defender exclusions"** that runs
`Add-MpPreference -ExclusionPath <install folder>`. This stops Defender from deleting `v2ray.exe`.
It only affects Defender and is removed on uninstall.

## 4. Third-party AV (360 / Huorong / Tencent) — whitelist + submit
Defender exclusions don't cover these. For the few machines that run them:
- Restore `v2ray.exe` from quarantine and add the install folder to that product's whitelist
  (end-user steps are in `USER_MANUAL_ZH.md` §4.3).
- Submit the signed installer as a false positive to the vendor so future versions clear
  automatically. Signing (layer 2) is what makes these submissions stick.

## macOS
No Apple Developer ID ($99/yr) is used, so builds are ad-hoc signed and **not notarized**.
Gatekeeper marks the downloaded `.dmg`/`.app` as quarantined. On each Mac, once:

```bash
xattr -dr com.apple.quarantine /Applications/SecureGateway.app
```

Or right-click the app → **Open** the first time. If you later buy an Apple Developer ID, set
`SIGN_ID` for `build-mac.sh` and notarize (steps printed at the end of that script) to remove this.

---
**Net effect:** layers 1–3 clear Windows Defender (the common case) with no recurring cost;
layer 4 handles the domestic AVs on the handful of machines that use them.
