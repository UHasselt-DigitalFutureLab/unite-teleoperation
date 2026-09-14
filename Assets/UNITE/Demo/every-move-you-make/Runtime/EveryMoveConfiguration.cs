using UnityEngine;

namespace Unite.Demo.EveryMoveYouMake
{
    [CreateAssetMenu(menuName = "UNITE/Every Move/Vehicle Configuration")]
    public sealed partial class EveryMoveVehicleConfiguration : ScriptableObject
    {
        [Header("TurtleBot3 Waffle Pi")]
        public float wheelbase = 0.287f;
        public float maxLinearVelocity = 0.26f;
        public float maxLinearAcceleration = 2.5f;
        public float maxAngularVelocity = 0.30f;
        public float maxWheelVelocityDifference = 0.0861f;
        [Header("Enhanced Moon disturbance model")]
        public float terrainRoughness = 0.7f;
        public float wheelSlipFactor = 0.15f;
        public float motorResponseVariation = 0.05f;
        public float wheelRadiusVariation = 0.02f;
        [Tooltip("Reference is ambiguous: 0.04 assigned in code, 0.03 shown by its old inspector range.")]
        public float encoderNoise = 0.03f;
        public float vibrationIntensity = 0.08f;
        public float vibrationFrequency = 3f;
        [Header("Body apex offset")]
        [Tooltip("Forward distance (metres, along heading) by which the visible body AND the " +
            "onboard camera mounted on it are shifted, as one rigid unit, from the kinematic " +
            "origin. The prediction overlays and pose feedback stay at the kinematic origin, so " +
            "a negative value pushes the body+camera back and makes the overlays appear to " +
            "spring from the robot's apex. Reference project value: -0.0765.")]
        public float meshForwardOffset = -0.0765f;
        [Header("Terrain coupling")]
        public float surfaceOffset = 0.005f;
        public float tiltResponsiveness = 8f;
        public float heightSmoothTime = 0.01f;
        public int deterministicSeed = 1;
        public bool useTimeDeltaInFixedUpdate = true;
    }

    [CreateAssetMenu(menuName = "UNITE/Every Move/Communication Configuration")]
    public sealed partial class EveryMoveCommunicationConfiguration : ScriptableObject
    {
        public float uplinkDelayMilliseconds = 2560f;
        public float downlinkDelayMilliseconds = 0f;
        public string direction = "uplink";
        public string temporalForm = "constant";
        public string affectedStream = "command";
        public string nonDelayDegradation = "none";
        public float DelaySeconds => uplinkDelayMilliseconds / 1000f;
    }

    [CreateAssetMenu(menuName = "UNITE/Every Move/Task Configuration")]
    public sealed partial class EveryMoveTaskConfiguration : ScriptableObject
    {
        public Vector3 targetCenter;
        public float targetRadius = 1f;
        public float timeLimitSeconds = 300f;
        public string targetShape = "Circle";
    }
}
