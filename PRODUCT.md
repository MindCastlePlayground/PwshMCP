# Product

<!-- impeccable:product-schema 1 -->

> Inferred from the executor brief, docs/BASELINE.md and docs/UI-CONTRACT.md. No interactive interview channel existed in this run; items marked (inferred) await user confirmation.

## Platform

Windows desktop (native WPF + WinForms tray, net8.0-windows). Not web; no impeccable mobile platform applies.

## Users

A single Windows developer/operator who lets capable AI coding agents (MCP clients) run PowerShell on their own machine. They trust the model, accept the risk of general administrator execution, and want to stop clicking UAC for every command. They are usually in another app (IDE, terminal, agent chat) when a request arrives. (inferred: one person, own machine)

## Product Purpose

PwshMCP gives AI agents PowerShell execution with a fresh environment per command. Administrator execution is gated by one human decision: a time-bounded grant approved through a single user-initiated UAC prompt. The tray is where that human sees requests, approves or denies them, revokes access, and watches tasks.

Success: the user notices a request without being interrupted, understands exactly who asks for what and what will run, approves in one deliberate act, and can end access instantly.

## Positioning

Grant-based delegation: one approval covers later commands from that client until sign-out or a chosen end time, instead of per-command UAC or a permanently elevated agent. Approval stays separate from UAC: UAC proves the human is admin; the approval screen states the scope.

## Operating Context

- Tray-bound runtime; the tray may host a hidden local broker when no service is installed.
- Requests arrive asynchronously; the tray icon changes, nothing takes focus, no automatic UAC.
- Grants: Pending, Active, Denied, Revoked, Expired. Jobs: NeedsApproval … OutcomeUnknown.
- Lock screen does not end a grant; Windows sign-out does.

## Capabilities and Constraints

- Normal tray can deny, revoke, cancel (A5 pending ACK); only an elevated approver process can approve.
- No silent success: every failure is shown with cause and next step.
- Never render environment variable values; output is shown only on explicit request.
- Labels from clients are plain text.
- No isolation or rollback promise for administrator scripts.

## Brand Commitments

Name "PwshMCP". No mascots. No stock app icons; icons are drawn in-house.

## Evidence on Hand

None beyond the repository. No customers, benchmarks or claims may be invented.

## Product Principles

1. Notice without interrupting.
2. Say exactly what will run and for how long before anything is granted.
3. Ending access is always one step away.
4. State is truth: never show Active, connected or done unless the broker says so.

## Accessibility & Inclusion

Keyboard-complete operation, readable contrast in light and dark Windows themes, high-DPI and resizable windows, minimal motion, states distinguishable without color.
