# Opponent locomotion

Paces use game-driven direct chase. Jog speed is 4, Run 6.5, Sprint 9 units/second.
Enter Run at distance <= 20 and Sprint at <= 8. On separation, leave Sprint above
8.5 and Run above 20.5. The hysteresis margin remains 0.5. Tackle distance is
canonically 1.4 in both the class default and config.json, matching prior runtime tuning.

Jog uses FootballPlayerAnimations from football_player.glb. Run uses
FootballPlayerRunAnimations from lowpoly_human_run_validation.glb. Sprint uses
FootballPlayerSprintAnimations from lowpoly_human_sprint_validation.glb.
Run and Sprint exports are loaded as ModelAnimations only. All clips animate the
same FootballPlayer model, with one deformable instance and independent playback
clocks per opponent. No asset data was edited.

Sprint uses its authored playback speed. Each clip change seeks once to the previous
clip's normalized phase (CurrentTime / (FrameCount / FramesPerSecond)); invalid or
zero durations fall back to phase zero. Initialization and Reset start at zero.
Repeated updates in the same pace advance continuously. The temporary Sprint -> Run
fallback is removed. Movement, facing and world position remain game-driven;
no root motion or blending framework was added.

Automated coverage includes mapping, unchanged speeds and thresholds, exact
hysteresis boundaries, reset, tackle radius, native compatibility of all three
clips, continuous mesh animation, isolated playback, world movement and facing.
Transitions cover Jog -> Run -> Sprint -> Run -> Jog on the same model instance.
The full controls and end-state suite also runs.

Manual visual smoke test (requires an interactive play session):
- Check distant Jog, medium Run and close Sprint at normal gameplay distance.
- Verify Sprint looks faster/more aggressive than Run.
- Create separation and check Sprint -> Run and Run -> Jog without boundary flicker.
- Watch simultaneous different clips and ensure Sprint does not reset every frame.
- Check facing, equipment, compact knee/ankle seams, sole clearance, foot sliding
  and root-motion drift.
- Play through tackles, touchdown, out-of-bounds, pause/resume and restart.

Hidden-window native tests do not replace visual review. Report visual issues
rather than automatically retuning gameplay values or authored cadence.
