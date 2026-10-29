# Puts the company code-signing key into GitHub so release builds are signed automatically.
#
# Run on the PC where the certificate was created (normal PowerShell, the same Windows user):
#   irm https://raw.githubusercontent.com/zheng-collab/custom-ssr/HEAD/scripts/export-signing-secrets.ps1 | iex
#
# You do not need the old .pfx file or its password: New-SelfSignedCertificate also kept the key
# in your user certificate store. This exports it again with a NEW random password, then either
# sets the two repository secrets with the GitHub CLI (if installed and signed in) or copies each
# value to the clipboard and opens the GitHub page to paste it. Nothing is written to disk except
# a temporary .pfx that is deleted straight away.

$ErrorActionPreference = 'Stop'
$Thumbprint = if ($env:SG_CERT_THUMBPRINT) { $env:SG_CERT_THUMBPRINT } else { '6E6EBC882DF3A6B2A6F322D4D9FC8F6D7302F84C' }  # signed v1.0.4
$Repo = 'zheng-collab/custom-ssr'
$SecretsPage = "https://github.com/$Repo/settings/secrets/actions/new"

function Say($m) { Write-Host "==> $m" -ForegroundColor Cyan }

# ---- 1. find the certificate with its private key ----------------------------------------
$cert = $null
foreach ($loc in 'CurrentUser', 'LocalMachine') {
    $store = New-Object Security.Cryptography.X509Certificates.X509Store('My', $loc)
    try {
        $store.Open('ReadOnly')
        $cert = $store.Certificates | Where-Object { $_.Thumbprint -eq $Thumbprint -and $_.HasPrivateKey } | Select-Object -First 1
    } catch { } finally { $store.Close() }
    if ($cert) { Say "Found the signing certificate in $loc\My: $($cert.Subject), valid until $($cert.NotAfter.ToString('yyyy-MM-dd'))"; break }
}

if (-not $cert) {
    Write-Host ''
    Write-Host "The signing certificate ($Thumbprint) is not in this Windows account." -ForegroundColor Yellow
    Say 'Looking for a SecureGateway-CodeSign.pfx file instead (this can take a minute)'
    $roots = @($env:USERPROFILE, "$env:SystemRoot\System32\custom-ssr") | Where-Object { $_ -and (Test-Path $_) }
    $files = foreach ($r in $roots) { Get-ChildItem -Path $r -Filter 'SecureGateway-CodeSign.pfx' -Recurse -ErrorAction SilentlyContinue }
    if ($files) {
        Write-Host 'Found these key files:'
        $files | ForEach-Object { Write-Host "   $($_.FullName)" }
        Write-Host 'They need the password chosen when the certificate was made. Import one with:'
        Write-Host '   Import-PfxCertificate -FilePath "<path above>" -CertStoreLocation Cert:\CurrentUser\My -Exportable'
        Write-Host 'then run this command again.'
    } else {
        Write-Host 'No key file found either. Most likely the certificate was created under another Windows'
        Write-Host 'user or on another PC: sign in there and run this command again.'
        Write-Host 'If it is gone for good, make a new one with scripts\make-selfsigned-cert.ps1 and re-trust it on every PC.'
    }
    return
}

# ---- 2. export it with a fresh password ----------------------------------------------------
$chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$bytes = New-Object byte[] 32; $rng.GetBytes($bytes)
$password = -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("sg-" + [Guid]::NewGuid().ToString('N') + '.pfx')
try {
    $secure = ConvertTo-SecureString $password -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath $tmp -Password $secure | Out-Null
    $base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($tmp))
} catch {
    Write-Host "Could not export the key: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'The key may have been created as non-exportable. Use the .pfx file instead (see scripts\make-selfsigned-cert.ps1).'
    return
} finally {
    Remove-Item -Force -ErrorAction SilentlyContinue $tmp
}
Say "Exported the key ($([Math]::Round($base64.Length / 1024, 1)) KB as text) with a new random password"

# ---- 3. hand both values to GitHub ---------------------------------------------------------
# Windows PowerShell 5.1 turns a native program's stderr into a terminating error under
# ErrorActionPreference=Stop, so call gh with Continue and judge it by its exit code only.
function Invoke-Gh {
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { & gh @args 2>&1 | Out-Null; return ($LASTEXITCODE -eq 0) }
    catch { return $false }
    finally { $ErrorActionPreference = $prev }
}
$ghReady = [bool](Get-Command gh -ErrorAction SilentlyContinue) -and (Invoke-Gh auth status)

if ($ghReady) {
    # --body, not a pipe: PowerShell would append a newline to the piped value.
    $ghReady = (Invoke-Gh secret set SIGN_PFX_BASE64 --repo $Repo --body $base64) -and
               (Invoke-Gh secret set SIGN_PFX_PASSWORD --repo $Repo --body $password)
    if ($ghReady) { Say 'Both secrets set with the GitHub CLI.' }
    else { Write-Host 'The GitHub CLI could not set the secrets; using the paste steps instead.' -ForegroundColor Yellow }
}
if (-not $ghReady) {
    Write-Host ''
    Write-Host 'Two short paste steps. Both are needed: the key and the password belong together.' -ForegroundColor Green
    Write-Host 'A GitHub page opens each time; sign in to GitHub if it asks.'
    $step = 0
    foreach ($s in @(@{ Name = 'SIGN_PFX_BASE64'; Value = $base64 }, @{ Name = 'SIGN_PFX_PASSWORD'; Value = $password })) {
        $step++
        Set-Clipboard -Value $s.Value
        Start-Process "https://github.com/$Repo/settings/secrets/actions"
        Write-Host ''
        Write-Host "  Step $step of 2:  $($s.Name)" -ForegroundColor Yellow
        Write-Host "  - If $($s.Name) is already in the list: click the pencil next to it."
        Write-Host '    Otherwise: click "New repository secret" and type the name above.'
        Write-Host '  - Click in the Secret box, press Ctrl+V (it is already on the clipboard),'
        Write-Host '    then click "Add secret" / "Update secret".'
        Read-Host  '  Press Enter here when GitHub shows it saved'
    }
    Set-Clipboard -Value ' '
    Say 'Clipboard cleared.'
}

Write-Host ''
Write-Host 'Done. Publish the next release (v1.0.6) the usual way; its Windows files will be signed' -ForegroundColor Green
Write-Host 'and SecureGateway-CodeSign.cer will be attached. You can check the secrets at:'
Write-Host "  https://github.com/$Repo/settings/secrets/actions"
