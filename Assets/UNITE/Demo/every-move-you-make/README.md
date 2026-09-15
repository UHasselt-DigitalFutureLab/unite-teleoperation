# Every Move You Make — UNITE reconstruction

This folder reconstructs the original study implementation using the thirteen UNITE
module slots. It targets Unity `6000.3.15f1`, uses a 50 Hz kernel loop (matching the reference
project's fixed timestep of `0.02` s), exact independently configurable uplink and downlink delays.
The retained reconstruction uses `2560 ms` uplink and `0 ms` downlink: the full `2.56 s`
round-trip delay is assigned to the command path.
The four conditions differ only in their selected downlink operator-side assistance component.

## Timing model: feedback reconstruction, local prediction

After Uplink Communication releases a command, module 5.7 first captures the current pose and remote
video before remote-side assistance processes that command. After the vehicle model applies the assisted
command, module 5.7 captures again; this final refresh replaces the earlier local observation and is the
only refresh sent through module 5.8, where each feedback stream receives its independently configured
downlink delay. The operator reconstructs the released package through module 5.9. These observations
represent remote time. Predictive output from module 5.10
represents operator time: for Path and Envelope it starts from the state captured with the currently
displayed video frame, integrates commands released after that capture, then integrates commands still
queued in the uplink. Network displays the scheduled command-path duration immediately from typed local
command history. Module 5.11 composites these prediction-only outputs over the reconstructed video.

The feedback connections branch after reconstruction: `5.8 -> 5.9 -> 5.11`, with
`5.9 -> 5.10 -> 5.11` supplying optional assistance. The kernel never forwards raw
received packages automatically from 5.9. Reconstruction explicitly publishes
`ReconstructedFeedbackContract` payloads through `PublishReconstructedFeedback`;
unknown or incomplete inputs may produce no output. The demo exposes a
`ReconstructedVideoFrame` buffer directly to presentation and publishes copied
`ReconstructedPose` and `ReconstructedMotionState` values. The latter uses the
`reconstructed-view-state` stream and preserves the video exposure's sample time
and sequence, so prediction needs no image pixels or transport package. The
reconstruction module retains ownership of the video texture and releases it
when replaced; consumers read the current buffer each presentation step.

Downlink payload lifetime is separate from delivery timing. `Package.Dispose()`
releases transport ownership; `RetainPayload()` returns a lease for a local
observation or reconstructed buffer that still needs the payload. The payload's
`IDisposable` hook runs only after the original ownership and all leases end.
Downlink rejects/drops and queue shutdown release ownership; reconstruction
releases each input after processing and retains a lease for its displayed frame.
Local observation leases survive dropped transmissions and expire on replacement
or destruction. This also covers local-only streams and queued, unprocessed frames.
Do not dispose a borrowed payload directly or replace `Package.Payload` after
publication. The Kernel knows only ownership; the demo's payload hook recycles
the texture. Other study pipelines must manage ownership explicitly as needed.

Both reconstruction and assistance can supply presentation in the same tick.
`NoAssistance` emits nothing; the base video does not depend on an assistance
pass-through. A technique that replaces or hides base feedback must declare how
its presentation implementation interprets that output. Returning no assistance
output alone never suppresses the reconstructed video.

Prediction objects use the `OperatorPrediction` layer, which the remote camera excludes, so they can
never be captured and delayed as part of the remote video.

## Run the bundled demo

After installing Git LFS and opening the project in Unity `6000.3.15f1`, open any
`Scenes/condition-*.unity` scene and press Play. Use the arrow keys to drive.
The four conditions are already configured; scene generation is only needed when
changing the shared apparatus. Exit Play Mode before opening another condition.

For a standalone player, put the desired condition first in Build Profiles / Scene
List and build for your installed target. The default build starts with Baseline;
there is no runtime condition selector. A running trial ends at target entry or
after 300 seconds. Logs are written under `Application.persistentDataPath`.

Run the EditMode tests in Window > General > Test Runner before publishing a
modified study. Also smoke-test all four scenes in Play Mode: video must remain
visible in Baseline, Network must show command timing, Path and Envelope must
align with video, and a completed or timed-out trial must write its CSV. Passing
compilation or scene checks alone does not verify rendering or player behavior.

## 1. Create the shared configuration assets

The scene generator creates the default Vehicle and Communication assets automatically when they are
missing and assigns the Vehicle asset to `TurtleBot3WafflePiEnhanced`. You may also create them
manually under `Assets/UNITE/Demo/every-move-you-make/Configurations` using the Create menu:

1. **UNITE > Every Move > Vehicle Configuration**, name it `TurtleBot3-WafflePi-Moon`.
   Keep the serialized defaults: wheelbase `0.287`, max velocity `0.26`, acceleration `2.5`, max
   omega `0.30`, wheel difference `0.0861`, roughness `0.7`, slip `0.15`, motor variation `0.05`,
   radius variation `0.02`, encoder noise `0.03`, vibration `0.08` at `3 Hz`, surface offset
   `0.005`, tilt responsiveness `8`, height smoothing `0.01`, seed `1`.
2. **UNITE > Every Move > Communication Configuration**, name it `Fixed-Uplink-2560ms`.
   Keep uplink `2560`, downlink `0`, direction `uplink`, temporal form `constant`,
   affected stream `command`, degradation `none`.
3. **UNITE > Every Move > Task Configuration**, name it `Lunar-Target-300s`. Set the target centre
   and radius to match the target marker you place in the scene; keep shape `Circle` and limit `300`.

The encoder setting is intentionally `0.03`: an explicit reconstruction choice,
not a calibration value reported in the original paper. The original source used
`0.04` despite an Inspector range ending at `0.03`, so exact calibration equivalence
is not claimed. No runtime method overwrites any configuration field.

## 2. Build the shared scene hierarchy

Create an empty scene named `condition-baseline` under this folder and add:

```text
EveryMoveYouMake
├── Environment
│   ├── MoonSurfaceTest
│   ├── Directional Light
│   └── TargetMarker
├── TurtleBot3
│   └── RobotCamera
├── PredictionVisuals
│   ├── CentreTrace
│   ├── LeftTrace
│   ├── RightTrace
│   └── NetworkTimeline
├── SupportModules
│   ├── PoseSource
│   ├── RobotStateSource
│   ├── RobotViewSource
│   ├── TargetRegionEntry
│   ├── ElapsedTimeLimit
│   └── TrialLogger
└── TeleroboticsAgent
```

Assignments:

1. Drag `Assets/UNITE/Demo/every-move-you-make/Art/Moon/moon-surface-test.fbx` into `MoonSurfaceTest`. Add a `MeshCollider` using the
   imported mesh. It is a query surface; do not add a Rigidbody.
2. Drag `Assets/UNITE/Demo/every-move-you-make/Art/Turtlebot/turtlebot3-waffle-pi.fbx` into `TurtleBot3`. Do not add a Rigidbody,
   WheelCollider, or any PhysX drive component.
3. Parent `RobotCamera` to the robot and add a Camera. Recover its transform and FOV from the supplied
   reference scene if exact camera matching is required; the publication does not report them. This
   is the vehicle's camera sensor: it renders into an off-screen texture and never directly to the
   operator display. `TurtleBot3WafflePiEnhanced` captures the sensor frame and includes its latest
   frame handle in `TurtleBotSnapshot.Sensors.Camera`. `RobotViewSource` projects that vehicle-owned
   sensor payload into `robot-view`; module 5.9 reconstructs the released local frame after downlink
   delay. The generator disables leftover template cameras.
4. Set the skybox to `Assets/UNITE/Demo/every-move-you-make/Art/Materials/Skybox.mat`. Use one Directional Light only. Disable fog and
   atmospheric/environment effects.
5. Give each trace object (`CentreTrace`, `LeftTrace`, `RightTrace`) a `MeshFilter` + `MeshRenderer`,
   plus one `UncertaintyRegion` object with the same. Assign `center.mat` to CentreTrace, `wheel line.mat`
   to LeftTrace/RightTrace, and `region.mat` to UncertaintyRegion. The presentation rebuilds these
   meshes at a presentation-only refresh rate: predicted poses are projected onto the Moon surface and
   drawn as terrain-following ribbons (Path) or the study's offset-boundary region with its curved
   extrema end cap (Envelope). With the retained zero-downlink reconstruction, prediction begins at the
   latest post-control observation released to the operator and integrates commands still pending in the
   uplink. This keeps the display predictive while projection raycasts remain outside the 50 Hz control
   workload.
   Keep all four empty initially. The scene generator creates and assigns them automatically.
6. Build `NetworkTimeline` as a Canvas assigned to the presentation. At runtime it becomes a
   `ScreenSpaceOverlay` using the reference `1158 x 650` layout, so terrain cannot clip it or change
   its apparent size. It recreates the four pipeline start/end coordinates retained in the study
   scene: up `(-80,-112)` to `(80,-112)`, down `(-80,-144)` to `(80,-144)`, left
   `(-262,-128)` to `(-102,-128)`, and right `(102,-128)` to `(262,-128)`, scaled by `2.15` to
   match the study capture. While a key is held, pooled blocks traverse its pipeline over the
   configured command delay. `CommandTimelineAssistance` derives semantic direction events from the
   typed mapped-command history; Presentation only maps those events to the configured layout. The
   generator creates and assigns the canvas and reads the delay from the communication asset.
7. A `TargetMarker` object with a `MeshFilter`/`MeshRenderer` shows the goal. It is authored into each
   generated condition scene as a multi-ring terrain-conforming disc at the configured
   `targetCenter`/`targetRadius`, using the study-green unlit `Target.mat`. The task module never owns
   or mutates this visual. `TargetRegionEntry` succeeds only when every rover-bounds corner is inside
   the circle including the study's `0.15 m` detection tolerance.
8. Place `TargetMarker` at the same world position stored in `Lunar-Target-300s`.

## 3. Add support components

Add these components to the correspondingly named objects and assign fields:

- **PoseSource**: `vehicle` = the `TurtleBot3WafflePiEnhanced` component added in step 4. Set its
  inherited Stream Id to exactly `robot-pose`.
- **TurtleBot3WafflePiEnhanced**: `cameraSensor` = RobotCamera; capture `960 x 540` at `20 Hz`.
  The vehicle model owns the camera sensor and publishes its latest frame through
  `TurtleBotSnapshot.Sensors.Camera`.
- **RobotViewSource**: `vehicle` = the vehicle model. It projects the camera sensor from the
  vehicle snapshot into the `robot-view` stream; it does not own or capture a second camera.
- **TargetRegionEntry**: `authoritativeRobot` = TurtleBot3; `configuration` = `Lunar-Target-300s`.
- **ElapsedTimeLimit**: `configuration` = `Lunar-Target-300s`.
- **TrialLogger**: `vehicle` = the vehicle component; `communication` = `Fixed-Uplink-2560ms`;
  `writeCsvOnTrialEnd` = true. CSV is written to `Application.persistentDataPath` when the trial ends.

## 4. Configure `TeleroboticsAgent`

Every selected UNITE module component in this section must be on the single GameObject named exactly
`TeleroboticsAgent`. Add the following components:

1. `TeleroboticsAgent`: ID `Participant`, Update Rate `50`.
2. `KeyboardArrowProvider`: declared poll rate `50`. It uses the Input System's named Up, Down,
   Left, and Right Arrow controls directly, so legacy `KeyCode` integers cannot corrupt bindings.
3. `ArrowToWheelVelocityMapping`: forward `0.26`, turn wheel velocity `0.0861`, inner-wheel scale
   `0.6`. Set inherited Output Stream Id to exactly `wheel-velocity-command`.
4. `UplinkCommunicationModule`: add one channel. Stream Id `wheel-velocity-command`. For Condition,
   choose `EveryMoveFixedDelayCondition`; assign `Fixed-Uplink-2560ms` and leave `Use Configured
   Downlink Delay` off. The exact configured value is scheduled without tolerance subtraction.
5. `SlopeBoundaryGuard`: terrain = MoonSurfaceTest MeshCollider; vehicle = TurtleBot3 is retained
   only as a first-tick fallback; normal position and heading input comes from the locally captured
   `robot-pose` observation package. Maximum slope is `21.80141°`. This is the explicit angular
   equivalent of the retained `0.08 m` rise over the `0.20 m` look-ahead; the serialized field directly
   controls the guard.
6. `TurtleBot3WafflePiEnhanced`: optional in the source scene. When absent, the generator adds it to
   `TeleroboticsAgent`, creates/assigns `TurtleBot3-WafflePi-Moon`, and binds objects named exactly
   `TurtleBot3` and `MoonSurfaceTest`. `MoonSurfaceTest` must already have its MeshCollider.
7. `RemoteObservationAndStateCapture`: Sources size `3`; assign PoseSource, RobotStateSource, and
   RobotViewSource. It
   captures once after uplink release and before remote-side assistance, then again after the vehicle
   model updates. The final refresh replaces the earlier local package and is sent to the configured
   downlink channels.
8. `DownlinkCommunicationKernel`: add two channels, `robot-pose` and `robot-view`. Give each an
   `EveryMoveFixedDelayCondition`, assign `Fixed-Uplink-2560ms`, enable `Use Configured Downlink
   Delay`. Both therefore resolve explicitly to `0 ms`. Each channel also exposes an optional
   non-negative delay override so pose and video can be delayed independently.
9. `EveryMoveStateReconstruction`: select it as module 5.9. It reconstructs released pose and video
   packages into local operator-side representations and never renders or accesses authoritative state.
10. Do not add a visualization assistance component to the source scene. The condition generator adds
   and wires exactly one implementation in each generated scene. If you already added one, that is
   also accepted and its shared references are preserved.
11. `RoverViewPresentation`: centre/left/right traces = their MeshFilters; uncertainty region =
   UncertaintyRegion MeshFilter; terrain = MoonSurfaceTest MeshCollider (for projection); network
   timeline root = NetworkTimeline; arrow images/sprites = the four NetworkTimeline arrows. Keep
   trajectory refresh rate at `20 Hz`. Presentation creates a full-screen reconstructed-video surface and
   a separate local prediction-only render layer. The prediction camera uses the matrices stored with
   the released video frame, so current predictions align with delayed imagery without entering it.
12. `TaskGoalAndTerminationModule`: Criteria size `2`; assign TargetRegionEntry then ElapsedTimeLimit.
13. `DataCaptureAndLoggingModule`: Capture Implementations size `1`, assign TrialLogger. Store the
   three descriptive metric definitions generated by `EveryMoveMetricDefinitions.Create()`;
   their formulas are documentation for downstream analysis, not executable calculations.

Finally wire the fields on `TeleroboticsAgent` itself:

| Agent field | Assignment |
|---|---|
| Input Provider | KeyboardArrowProvider |
| Command Mapping And Encoding | ArrowToWheelVelocityMapping |
| Operator Side Assistance | None |
| Uplink Communication | UplinkCommunicationModule |
| Uplink Remote Side Assistance | SlopeBoundaryGuard |
| Vehicle Robot Model | TurtleBot3WafflePiEnhanced |
| Remote Observation And State Capture | RemoteObservationAndStateCapture |
| Downlink Communication | DownlinkCommunicationKernel |
| Operator-side State Reconstruction | EveryMoveStateReconstruction |
| Downlink Operator Side Assistance | None in the source; generator assigns the scene-specific component |
| Operator Presentation | RoverViewPresentation |
| Task Goal And Termination | TaskGoalAndTerminationModule |
| Data Capture And Logging | DataCaptureAndLoggingModule |

## 5. Generate the four independent condition scenes

Open and save the fully configured source scene (for example `template`), then run:

**UNITE > Every Move You Make > Generate Four Condition Scenes**

The source scene does not need `NoAssistance`; creating the four visualization components is the
generator's responsibility. It derives Uplink and Vehicle Configuration from the configured scene
and uses the single `EveryMoveCommunicationConfiguration` asset in the project. The editor tool
reopens the same saved source before producing every copy, removes any existing assistance component,
adds exactly one condition component, assigns the same Uplink, Vehicle, and Communication assets, and
rewires only `Downlink Operator Side Assistance`. It writes these four
independently executable scenes under `Scenes/`:

The currently open saved scene is the authoring source. The generator fully configures it, saves it as
`condition-baseline`, and then treats that saved baseline as the single source of truth for all three
copies. Author the recovered rover and terrain transforms in the open source scene; the generator
preserves those transforms. It also reapplies the recovered source-study Directional Light:
position `(11.170045, 20.060133, -14.355915)`, source quaternion
`(-0.22319584, 0.32387382, -0.88775796, -0.2391135)`, scale approximately `100`, color
`(0.6037736, 0.6037736, 0.6037736)`, realtime intensity `0.5`, indirect multiplier `0.2`, soft
shadows at strength `0.2`, bias `0.05`, normal bias `0.4`, and near plane `0.2`.

| Scene | component selected on `TeleroboticsAgent` |
|---|---|
| `condition-baseline` | `NoAssistance` |
| `condition-network` | `CommandTimelineAssistance` |
| `condition-path` | `IdealTrajectoryAssistance` |
| `condition-envelope` | `WorstCaseEnvelopeAssistance` |

Do not edit a generated scene independently. If any shared object or setting changes, edit the source
scene and regenerate all four. Keep shared configuration assets shared within that study;
duplicate them when creating an independent study so edits do not alter the bundled conditions.
For a standalone build, select the desired condition as the first enabled scene.
URL/runtime condition switching is intentionally absent. Set the agent's ID in the
Inspector before each participant run; its deterministic seed is derived from that ID.

## 6. Module-to-script inventory

| UNITE module | implementation |
|---|---|
| Input Provider | `KeyboardArrowProvider` |
| Command Mapping and Encoding | `ArrowToWheelVelocityMapping` |
| Uplink operator assistance | none |
| Uplink Communication | kernel + `EveryMoveFixedDelayCondition` |
| Uplink remote assistance | `SlopeBoundaryGuard` |
| Vehicle/Robot Model | `TurtleBot3WafflePiEnhanced` |
| Remote Observation and State Capture | kernel + `PoseSource`, `RobotStateSource`, `RobotViewSource` |
| Downlink Communication | kernel + two fixed-delay `EveryMoveFixedDelayCondition`s |
| Operator-side State Reconstruction | `EveryMoveStateReconstruction` (released pose and immutable video-frame representations) |
| Downlink operator assistance | one of the four scene-specific implementations |
| Operator Presentation | `RoverViewPresentation` |
| Task Goal and Termination | kernel + `TargetRegionEntry`, `ElapsedTimeLimit` |
| Data Capture and Logging | kernel + `TrialLogger` |

## 7. Retained analysis definitions

- Completion time: trial start to target entry, seconds, capped at 300.
- Pause count: inter-input gaps greater than or equal to the communication asset's uplink delay (2.56 s).
- Completion rate for timeout only: nearest of 101 post-hoc successful-reference-trajectory points;
  index divided by 100. The reference trajectory is analysis data, not a logger dependency.
- The event CSV records participant ID, deterministic seed, selected condition, configuration snapshot,
  raw input, mapped command, uplink/downlink release, assistance output, authoritative state and the
  terminal event. Questionnaire and counterbalancing tooling must use additional declared capture
  implementations when reproducing the complete experimental protocol rather than the apparatus alone.

## 8. Structural checks before Play Mode

Without running Unity, verify:

- exactly one active `TeleroboticsAgent` exists and every selected module is attached to it;
- all stream IDs match the exact strings above;
- all four scenes reference the same three configuration assets;
- the robot has no Rigidbody or wheel colliders;
- the rover sensor Camera and the black output Main Camera are enabled, and the Main Camera's
  AudioListener is disabled;
- both downlink channels are explicit and use the configured 0 s delay;
- the selected State Reconstruction module is present and the rover sensor Camera is assigned to
  `TurtleBot3WafflePiEnhanced`;
- the remote Camera excludes the `OperatorPrediction` layer;
- the only condition difference is the selected assistance component;
- the Path and Envelope predictors read the uplink queue and reconstructed video-aligned motion values, not the robot
  Transform; and
- no startup code assigns configuration values.

In Play Mode, verify that `TurtleBot3WafflePiEnhanced` generates and assigns the off-screen
`EveryMoveVehicleCameraSensor` RenderTexture to the rover sensor Camera.

The exact imported source assets live under `Assets/`; the contract implementation lives under
`Runtime/`.
