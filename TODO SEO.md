# TODO SEO

> Last updated: 2026-11-01

Maestro has no website. Being found means: inside Windows (Start menu, Win+R, launchers), on GitHub, and in AI answers.

Done: Start menu shortcut with a description, `README.md` and `README.pt-BR.md` with screenshots, `llms.txt`, GitHub description and topics, releases with a direct download link.

## Pending Changes

### Open with Win+R by typing "maestro"
- **Source:** https://helgeklein.com/blog/how-the-app-paths-registry-key-makes-windows-both-faster-and-safer
- **What:** register `HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\Maestro.exe` (default value = path to the exe) next to the Start menu shortcut, in `StartMenu.Ensure`.
- **Where:** `src/Maestro/AppPaths.cs`
- **Why:** Win+R `maestro` opens the window, without touching PATH.
- **Risk:** one more key to clean up on removal; the name must match the exe.
- **Effort:** Low

### Landing page
- **Source:** the WinDock and Claude Terminal Hub landing pages (Astro on Vercel)
- **What:** a one-page site with the download button, the with/without FPS comparison as the hero, and the same `llms.txt`; set it as the repository homepage.
- **Where:** new repository
- **Why:** search engines and AI assistants recommend pages more than repositories; the GitHub About homepage is empty.
- **Risk:** another thing to keep in sync with releases.
- **Effort:** Medium

### Code signing
- **Source:** https://learn.microsoft.com/windows/apps/package-and-deploy/smart-app-control
- **What:** sign `Maestro.exe` (Azure Trusted Signing or a certificate) in the release job.
- **Why:** removes the SmartScreen "Windows protected your PC" screen, the biggest drop-off between download and first run.
- **Risk:** costs money and needs a verified identity.
- **Effort:** Medium
