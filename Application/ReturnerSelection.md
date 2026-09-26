# Returner player cards

The Returners menu remains a ListWithDetail menu in menu.json. MenuManager owns
selection, keyboard/gamepad navigation, scrolling, Back and SelectReturner actions.
ReturnerSelectionView draws the presentation and maps mouse coordinates into the
native list viewport only during MenuManager.Update.

Menus 0.1.3 exposes no renderer or layout customisation hook. The adapter queries
CurrentLayoutBounds and GetItemBounds through a small read-only reflection bridge;
it never changes private menu state. Keep the mouse/scrolling tests when updating
that package, or replace the bridge with public layout hooks when available.

The left list is 174 px at 640x480 and capped at 300 px at 1280x720. Both panels
start at y=100 and reserve 48 px below for navigation hints. The compact window
retains native scrolling. The detail card places jersey/name and profile height
(metres)/weight (kilograms) above five numeric rating bars and a full-height model.
No duplicate body dimensions or gameplay ratings are stored in the UI.

Accent colours are sampled once from the selected uniform atlas, above its chest
number, and lifted for dark-background contrast. Preview framing uses the animated
mesh envelope across every taunt frame, cached per returner; it resets to frame zero
on highlight changes and loops as before. The orthographic camera is stable through
each taunt, with a subtle floor disc and contact shadow.

Validation:
- dotnet build -c Release
- dotnet test Tests/Controls.Tests.csproj -c Release
- ReturnerMenuTests render five returners at both 640x480 and 1280x720, check six
  poses each for visible player pixels, edge clearance and substantial height,
  and verify native keyboard/gamepad flow and transformed mouse selection.
- Ten screenshots are exported to artifacts/returner-select/ (ignored by Git).

Manual review: inspect the screenshots and live taunts for typography, colour
balance and framing; verify scroll-wheel feel and a physical controller at both
resolutions. Pixel checks cannot judge those aesthetic details.
