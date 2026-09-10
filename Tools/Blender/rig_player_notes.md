# Stage 6 rig inspection

Run `rig_player.py` through Blender MCP `run_blender_script` with the repository as project_root. It opens `Assets/Models/lowpoly_human_stage6.blend`, builds the rig, renders previews, and saves `Assets/Models/lowpoly_human_rigged.blend`. No manual weight painting or other manual steps were used.

The 20-bone FK hierarchy uses Root > Hips > Spine > Chest > Neck > Head; Chest > Clavicle.L/R > UpperArm.L/R > LowerArm.L/R > Hand.L/R; Hips > UpperLeg.L/R > LowerLeg.L/R > Foot.L/R. No IK, constraints, toe bones, or gameplay actions. Local Y follows bone length; roll references world +Z. World +Y remains up, -Z forward, +X character left. Root and armature origin are (0,0,0); rest soles are Y=0. Pose grounding translates Hips, never Root.

Skin uses normalized anatomical ramps with at most two bone influences per vertex and standard linear deformation, without Blender-only preserve-volume skinning. Source mesh coordinates, polygon topology, and flat shading remain intact. HelmetAssembly is rigidly parented to Head and contains Helmet, Facemask, Visor, and ChinStrap. ShoulderPads remains a separate mesh: chest/back/wrap rigid to Chest; each cap/padding region rigid to its Clavicle.

Select PlayerRig, use the Action Editor action dropdown, and choose a Pose_* action at frame 1. Each action has fake-user retention and one keyed frame. Neutral is the saved default. These are inspection snapshots, not animation clips.

## Visual findings and lock recommendation

All eight poses were inspected in front and three-quarter views:

- Neutral: approved silhouette and equipment alignment preserved.
- Athletic: hips lower about 13.6 cm; knees bend and soles remain grounded. The segmented knee/ankle bands remain conspicuous.
- KneeRaise: pronounced hip flexion and 105-degree knee bend expose the overlapping thigh/groin construction; inner knee compression and a hard joint transition are visible.
- ArmsForward: shoulder volume broadly holds; caps retain volume but their seam with the chest plate opens. Elbows retain angular shape.
- CrossBody: the arm crosses the chest; extreme adduction causes upper-arm/chest-pad penetration. This pose is a stress-test failure requiring correction before animation lock.
- TorsoTwist: hips stay forward; waist shows modest narrowing and faceted shear. Pads remain rigid and rotate with Chest.
- HeadLean: helmet, visor, facemask and strap stay together. The short neck shows an angular compressed inside edge, without gross collapse.
- ShoulderDip: rigid caps and chest follow the shoulder slope; cap seams remain visible. Lower body stays grounded.

Recommendation: keep this as a reproducible deformation review rig; do not lock for Run yet. Resolve the cross-body pad/upper-arm clearance and review deep hip/knee compression first. The preserved source consists of separate overlapping anatomical volumes, so seam-free extreme bending may ultimately require localized joint topology work. No redesign or topology changes were made in this pass. No GLB was exported; runtime skinning/export has not been validated.

## Outputs and checks

`Assets/Models/RigPreviews/` contains 16 PNGs: `Pose_{Neutral,Athletic,KneeRaise,ArmsForward,CrossBody,TorsoTwist,HeadLean,ShoulderDip}_{Front,FrontThreeQuarter}.png`, plus `validation.json` with source SHA-256, full hierarchy and per-pose grounding measurements.

Generator assertions check unchanged source geometry and source file hash, normalized weights, bone count, and support soles within 0.00001 m of Y=0. Reopened saved-file checks verified identity armature, root origin, neutral default, rigid helmet parenting, and exactly eight single-frame Pose_* actions. Existing C# / project / configuration contents were hashed before and after; zero changes were found. No commit or push.
