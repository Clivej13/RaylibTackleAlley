# Directional returner movement

WASD / the left stick choose camera-relative travel in ThirdPerson: up moves into
the scene along the camera's horizontal forward direction; down moves toward the
camera; left/right move across the view. Pitch does not change movement speed.
Close/Medium/Far retain world-space controls: W/up is downfield (-Z), S/down is
toward the returner's own end (+Z), A/left is -X and D/right is +X.
Diagonal input is capped at unit length, so it cannot increase the selected tier's
top speed. Analogue magnitude scales the target speed.

No command starts or sustains automatic running. Reset starts at rest; releasing the
controls decelerates along the last heading using ForwardDeceleration. Sprint alone
cannot move a stationary returner. Hold Shift / RT plus a direction to sprint.
Since S now moves backward, jog is a separate rebindable Slow action (Left Ctrl / LT).

PlayerInputSnapshot captures both movement axes, Slow, Sprint and gestures once per
rendered frame. ThirdPerson resolves the movement axes against the displayed camera
yaw once before substeps. BallCarrier consumes that immutable world-space command;
it never reads a camera. Switching modes alone never changes physical state; subsequent
directional input follows the newly selected control frame. DirectionalMovement supplies consistent full-circle yaw,
shortest-angle turns and forward/right axes for movement, contact and camera follow.

The existing role/rating tier speeds and acceleration rates are unchanged. Scalar
CurrentForwardSpeed now means speed along the chosen heading, rather than speed along
world -Z. Run steering retains Agility's DirectionBlend; sprint uses the existing
SteeringResponse as an angular rate (90 degrees per input unit, giving a nominal
180-degree reversal in 0.6 seconds). Facing follows the heading with FacingResponse.
Sharp reversals use the existing speed loss and bounded acceleration lockout; the
old strong-left/right threshold is interpreted as an angle about any incoming heading.

Sprint-release cuts measure sides against the heading at the start of the sprint
session. The release window survives neutral input and sprint re-presses. Cut handoff
uses the current movement heading rather than hard-coded world +/-45 degrees.
Jukes and spins capture the returner's local right axis and entry speed. Changing
input during an evade cannot redirect its captured displacement or increase its
distance. Existing durations, speed retention, sprint cancellation and gesture
recognition remain. Evades cannot create momentum from rest.

Upright contact splits impulses along/across the movement heading; there is no world
-Z projection or +/-45-degree clamp. Swept body contact, tackle decisions, reach,
committed correction, ragdoll handoff and recovery are unchanged. Recovery resumes at
rest. Scripted running into the end zone occurs only after a touchdown.

The old automatic -Z travel, S-as-jog, additive world-X lateral speed and lateral
forward-retention multiplier no longer drive gameplay. Legacy PlayerLateralSpeed,
PlayerMaxRunYawDegrees and FullSteeringForwardRetention config keys are still accepted
for existing config files, but do not control this directional motor.

Tests cover keyboard/controller sampling, all cardinal/diagonal headings, starts/stops,
180-degree sprint/run reversals, rotated cuts/evasions/contact, camera follow/orbit/zoom,
and identical simulation with camera changes. Native rendering tests exercise authored
clips and football attachment. Hands-on checks remain for turn/cut animation feel,
world-space controls while orbiting, sprint turn radius, and tackle readability.
