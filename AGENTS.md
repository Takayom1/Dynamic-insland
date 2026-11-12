# Local development workflow

Takayom1 requested one local testing version instead of repeated installers.

- Keep the current project version while iterating locally; change it only for an explicitly requested release. The approved release is 0.17.0.
- Do not commit, push, create tags/releases, upload to GitHub, or build new installers unless the user explicitly asks after approving the result.
- Use `scripts/update-local.ps1` for local builds and graceful restarts. Never force-kill an active assistant turn or overwrite running binaries.
- Preserve account/history, selected vault, tasks, voice models and calibration outside the application directory.
- Branding is Hoshino Island. Preserve required original copyright/licensing notices in source and the readable Licenses and authorship section.
- See `DEV-WORKFLOW.md` for the local update procedure.
