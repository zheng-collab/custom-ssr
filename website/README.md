# Company VPN page for fourthzodiac.com

`company-vpn.html` is a self-contained, bilingual (中文 / English) page that tells staff where to
download SecureGateway and how to set it up on Windows, macOS, Android and iPhone. It has no
scripts and no external stylesheets, so it can be dropped into any host.

Every download button uses GitHub's *latest release* redirect, e.g.

```
https://github.com/zheng-collab/custom-ssr/releases/latest/download/SecureGateway-Setup.exe
https://github.com/zheng-collab/custom-ssr/releases/latest/download/SecureGateway-win-x64.zip
https://github.com/zheng-collab/custom-ssr/releases/latest/download/SecureGateway-macos-arm64.dmg
https://github.com/zheng-collab/custom-ssr/releases/latest/download/SecureGateway-macos-x64.dmg
https://github.com/zheng-collab/custom-ssr/releases/latest/download/USER_MANUAL_ZH.pdf   (from the next tagged release on)
```

so the page never needs editing when a new version is tagged.

## Putting it on the site

* **Static host / Vercel / Netlify:** copy the file to `public/company-vpn.html` (or the folder your
  host serves) and it is live at `https://fourthzodiac.com/company-vpn.html`. Add a rewrite from
  `/company-vpn` if you want the URL without `.html`.
* **Next.js / React:** create `app/company-vpn/page.tsx` and paste the contents of `<main>` as JSX,
  or serve the file from `public/` as above.
* **WordPress / CMS:** create a page "Company VPN", switch the editor to HTML/Code view and paste
  everything inside `<main>…</main>`. Paste the `<style>` block into the page's custom CSS.
* **Existing "Company VPN" page:** replace its download links with the five URLs above and link
  to `https://github.com/zheng-collab/custom-ssr/releases/latest` as "All versions".

## Notes

* If the GitHub repository is **private**, the release links only work for people signed in to
  GitHub with access. Make the repository public, or host the installers on the site itself.
* The page deliberately does **not** contain the server link or QR code (that is the credential
  for phones). Staff get it from the desktop app's **Servers → Share QR** button or from IT.
* The "Forgot password" link expects `https://fourthzodiac.com/forgot-password` to exist and the
  Supabase **Site URL** to be `https://fourthzodiac.com` (see USER_GUIDE.md).
