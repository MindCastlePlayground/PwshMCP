# Design system · PwshMCP tray

Recorded from the built code in `src/PwshMcp.Tray` (ground truth over intention). Product context: [PRODUCT.md](PRODUCT.md). Surface spec: [docs/UI-DESIGN.md](docs/UI-DESIGN.md).

## World
Admin access as a **visitor badge**: requested at the desk, checked, issued with a visible validity window, revocable at any time. Windows-native utility register (Operate mode): system fonts, native task dialog and tray menu, no custom chrome.

## Mark
Rounded badge with a clip slot (16-unit geometry, `Glyphs.cs`, `Assets/badge.svg`). App icon: indigo gradient badge with a PowerShell prompt `>_`. Tray glyphs are monochrome to match the taskbar (white on dark bar, near-black on light bar), redrawn per pixel size, not scaled.

| Tray state | Shape rule (colour-independent) |
|---|---|
| Offline | outline + diagonal slash, dimmed |
| Idle | outline |
| Active | solid with punched slot |
| Pending (overlay) | solid amber dot top-right, cut out of the badge with a transparent ring |

## Colour (Restrained)
Neutrals follow the Windows app theme (`AppsUseLightTheme`), re-applied live on `WM_SETTINGCHANGE`. One indigo accent: primary action, selection, focus ring. State colours are always paired with a line rule:

| Role | Light | Dark | Paired shape |
|---|---|---|---|
| Waiting | #8A5300 on #FFF1D6 | #FFC46B on #3A2D14 | dashed outline |
| On / issued | #0B7A4E on #DFF4EA | #6BD9A8 on #15332A | solid dot |
| In progress | accent on accent-soft | | hollow ring |
| Refused / failed | #B3261E on #FDE7E5 | #FF8F86 on #3D1B1A | cross |
| Ended | muted on raised | | strikethrough |

Ground #F4F5F7 / #14161B · panel #FFF / #1B1E24 · raised #EEF0F3 / #242830 · line #DDE1E6 / #313640 · ink #171A21 / #ECEEF2 · muted #555E6C / #A0A8B6 · accent #3346C8 / #9AA6FF.

## Type
Segoe UI Variable Text → Segoe UI → Microsoft YaHei UI (CJK fallback) for everything; Cascadia Mono → Consolas → Microsoft YaHei UI only for code, paths and command previews. Scale: 12 label · 12.5 code · 13 secondary · 14 body/value · 16 product name · 22 detail title · 24 approval title. Weights 400/600. Tabular numerals on values. Body measure capped at 640 px.

## Space and shape
4-pt rhythm; detail pane 32 px padding, list 16 px; facts 36 px apart, sections 26 px above / 10 px below. Radii: controls 6, rows/code 8, badge card 12. Elevation: a single soft shadow only on the selected segment; everything else is flat with 1 px lines.

## Components
- **Buttons**: Primary (accent fill), Secondary (raised), Danger (red text; armed state = red-soft fill + red border), Quiet (text). All: hover veil, press veil, 45% disabled, 2 px accent focus ring offset 3 px, access keys.
- **Two-step confirm** (`Confirm`): first click arms for 4 s and renames the button ("Confirm revoke"), second acts. Used for Deny, Revoke, Revoke all, Cancel task. No modals.
- **Chip**: state label with shape mark (above).
- **Band**: validity bar; issued → now tick → end tick. Sign-out access fades out on the right (open-ended). In the approval window its length is proportional to the chosen duration.
- **Code box**: read-only, selectable, mono, scrolls both ways, max 240 px.
- **Segmented tabs**: Requests / Access / Tasks with live counts (Ctrl+1/2/3).

## Motion
Only the detail pane moves: 180 ms opacity 0.35→1 and 4 px rise, exponential ease-out, on selection change. Off when Windows animations are off. Tray never blinks.

## Copy
Chinese primary (technical identifiers such as PwshMcp.Host.exe and --backend-dir stay as-is). Actions are named by outcome ("批准并开始", "撤销授权"), with Windows access keys in the form "(_A)", errors state cause + next step, never claim success without a broker response. All strings in `Consts.cs → Copy`.

## Request notice window

- Keep Chinese and English notice windows in the same fixed content, lifetime choices, and action order; allow the content area to scroll while the footer stays fixed.
- Show the window without activating it or taking keyboard focus. Approval must not be the default Enter action.
- Manual and automatic Do Not Disturb remain independent controls; quiet mode defers notices without changing requests.
- Batch approval uses only the pending request snapshot captured when the tray menu opens.
