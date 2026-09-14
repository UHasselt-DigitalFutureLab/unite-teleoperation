using Unite.Kernel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unite.Demo.EveryMoveYouMake
{
    public sealed partial class KeyboardArrowProvider : InputProvider<ArrowInputPackage>, IVector2OutputSource
    {
        [SerializeField] private int declaredPollRateHz = 50;
        public event System.Action<Vector2> Vector2OutputProduced;
        public int DeclaredPollRateHz => declaredPollRateHz;

        protected override bool TryCaptureInput(double timestamp, out ArrowInputPackage output)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                output = null;
                return false;
            }

            // Use the named controls directly. Earlier versions serialized legacy
            // KeyCode values (for example 273 for UpArrow) into these scenes; those
            // integers are not valid Input System Key enum values.
            var upKey = keyboard.upArrowKey;
            var downKey = keyboard.downArrowKey;
            var leftKey = keyboard.leftArrowKey;
            var rightKey = keyboard.rightArrowKey;
            bool u = upKey.isPressed;
            bool d = downKey.isPressed;
            bool l = leftKey.isPressed;
            bool r = rightKey.isPressed;
            output = new ArrowInputPackage(timestamp, Time.deltaTime, u, d, l, r);
            Vector2OutputProduced?.Invoke(new Vector2((r ? 1 : 0) - (l ? 1 : 0), (u ? 1 : 0) - (d ? 1 : 0)));
            return true;
        }
    }

    public sealed partial class ArrowToWheelVelocityMapping
        : CommandMappingAndEncoding<ArrowInputPackage, WheelVelocityCommand>
    {
        [SerializeField] private float requestedForwardVelocity = 0.26f;
        [SerializeField] private float requestedTurnWheelVelocity = 0.0861f;
        [SerializeField, Range(0f, 1f)] private float innerWheelScaleWhileDriving = 0.6f;
        public float RequestedForwardVelocity => requestedForwardVelocity;
        public float RequestedTurnWheelVelocity => requestedTurnWheelVelocity;
        public float InnerWheelScaleWhileDriving => innerWheelScaleWhileDriving;

        protected override bool TryMapAndEncode(ArrowInputPackage input, double timestamp,
            out WheelVelocityCommand output)
        {
            int forward = input.Up ? 1 : input.Down ? -1 : 0;
            int turn = input.Left ? -1 : input.Right ? 1 : 0;
            float vl = forward * requestedForwardVelocity;
            float vr = forward * requestedForwardVelocity;
            if (forward != 0 && turn < 0) vr *= innerWheelScaleWhileDriving;
            if (forward != 0 && turn > 0) vl *= innerWheelScaleWhileDriving;
            if (forward == 0 && turn != 0) { vl = -turn * requestedTurnWheelVelocity; vr = turn * requestedTurnWheelVelocity; }
            output = new WheelVelocityCommand(input, timestamp, vl, vr);
            return true;
        }
    }
}
