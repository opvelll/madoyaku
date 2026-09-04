# Repository instructions

## Windows UI verification

- For every change that can affect WPF layout, styling, text, dialogs, focus, hotkeys, resizing, or other visible behavior, build the application and perform a real UI check before reporting completion.
- Use the `computer-use` skill and its Windows app automation workflow when it is available. Launch the newly built `HonnyakuKun.exe`, select the exact returned application window, and inspect a screenshot rather than relying only on successful compilation.
- Open every window or dialog touched by the change. Verify text/background contrast, clipping, wrapping, spacing, alignment, control states, and readability at the current Windows DPI setting.
- Exercise at least the primary interaction affected by the change and refresh the window state afterward. For a settings change, open the settings dialog and inspect the edited control with its current value visible.
- Never type, reveal, or transmit a real API key during automated UI checks. Do not trigger a live OpenAI request merely to test presentation.
- Close test instances after inspection, then run the Release build and regenerate the standalone `publish/HonnyakuKun.exe` when the UI check passes.
- If Windows computer-use is unavailable or fails after the documented recovery attempt, record the exact limitation and perform at minimum a warning-free build plus a launch/exit smoke test. Do not claim that visual QA was completed in that case.

## Required command checks

- Run `dotnet build -c Release`.
- Run `dotnet format HonnyakuKun.csproj --verify-no-changes --no-restore`.
- Run `git diff --check`.
