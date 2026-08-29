# Physical-host manual smoke

Initial manual smoke on a normal interactive Windows 11 host found the following. Subsequent focused retests confirmed the noted PASS items under the second table.

| Area | Result | Notes |
| --- | --- | --- |
| Global hotkey summon and toggle | PASS | The registered hotkey showed and hid Quick Search. |
| Query focus | PASS | Focus was in the query box on summon. |
| Escape and deactivation hide | PASS | Escape and click-outside/deactivation hid Quick Search. |
| Keyboard result flow | PASS | Up, Down, Home, End, and Enter behaved as required. |
| Theme | PASS | System, Light, and Dark switched correctly. |
| Settings persistence | PASS | Settings persisted across restart. |
| Manually typed custom hotkey | PASS | A custom hotkey remained effective after restart. |
| Mixed DPI and first summon after monitor move | PASS | 100% to 150%, centering, and first summon had no M08 regression. |
| Tray context menu | FAIL | The icon appeared, but right-click did not expose Show, Settings, or Exit. |
| Search box vertical alignment | FAIL | Query text and placeholder were visibly off-center. |
| DWM border | FAIL | A light approximately 1 px system border was visible. |
| Settings hotkey key capture | FAIL | Manual typing worked, but physical shortcut capture did not. |
| Taskbar and Alt-Tab exclusion | FAIL | Quick Search appeared in both surfaces. |
| Task Manager icon | FOLLOW-UP | Explorer and taskbar used Quail branding; Task Manager did not. |

## Focused retest

| Area | Result | Notes |
| --- | --- | --- |
| Taskbar exclusion | PASS | Quick Search no longer created a normal taskbar button. |
| Alt-Tab exclusion | PASS | Quick Search no longer participated in Alt-Tab. |
| Global hotkey and keyboard flow | PASS | The original smoke results remained good. |
| Mixed DPI, first summon, and centering | PASS | The original smoke results remained good. |
| Settings persistence | PASS | The original smoke result remained good. |
| Capture-only hotkey recorder | PASS | Captured another and the currently active combination, rejected arbitrary typing, and `Use default` worked. |
| Tray interactions | PASS | LMB, RMB menu, Show Quick Search, Settings, and Exit all worked. |
| White outer border | PASS | The `WS_DLGFRAME` workaround removed the visible outer white edge. |
| Search-box optical alignment and left spacing | ACCEPTED | Final `QueryBox.Padding = 4,3,0,0` was accepted for M10; no further visual polish is in scope. |
| Task Manager Processes icon | FOLLOW-UP | Explorer icon, PE resources, and runtime branding passed; Task Manager remained generic even with the approved 16 px frame. No M10 asset change is planned. |

The production lifecycle harness subsequently passed 500/500 real summon/Escape cycles with focus and resident-process checks; its durable summary is in `production-lifecycle.md`.
