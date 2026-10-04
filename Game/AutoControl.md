# Auto control

PlayerControlState distinguishes Player input from Auto scripted movement. AutoTendency selects the automatic behaviour; Returner is the first supported tendency. This is independent of defender AI.

Every game run and restart selects Auto + Returner. The runner accelerates downfield (world -Z) at speed tier 2 using the same rating-based movement and shared menu scaling as normal running. Camera orbit does not redirect Auto movement.

Movement input, sprint, jog, or an evade gesture transfers control to Player immediately and clears the active tendency. Pause and camera look do not transfer control. Releasing input after takeover decelerates to the standing animation; Auto resumes only on a new run. Menus do not advance simulation.

Standalone BallCarrier.Reset() retains direct player control; the game explicitly calls Reset(AutoTendency.Returner). Add future tendencies to AutoTendency and its command selection switch without adding new control states or AI logic.
