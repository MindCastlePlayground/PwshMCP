# Native window activation regression

Run `dotnet run --project tests/PwshMcp.Window.Tests` on Windows with an interactive desktop.

The test creates an off-screen window and uses a thread-local CBT hook to request
activation again while Windows is creating its HWND. It does not connect to the
Broker, request privileges, or change grants. It also checks hide/show and closing
to tray followed by reopening.

Before the `MainWindow.Present` guard, the native hook reproduces the reported
`ArgumentException` at `CompositionTarget.SetRootVisual`, through `Window.Show`.
Reentry from `SourceInitialized` alone is too late to reproduce the fault.
With the guard the same native hook and reopen checks pass. This establishes a
reproducible cause; it does not identify which native message caused the original
user incident.
