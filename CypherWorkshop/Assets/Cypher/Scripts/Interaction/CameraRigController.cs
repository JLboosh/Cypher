using UnityEngine;
using UnityEngine.InputSystem;

namespace Cypher
{
    /// <summary>
    /// Turns the seated view left/right (yaw, on the "CameraRig" object) and up/down (pitch, on
    /// its child Main Camera). Mouse dragging is fed in by InteractionController so that
    /// dragging a hologram can never move the camera at the same time.
    /// Keys: ←/→ turn, ↑/↓ look up/down, Home = back to the default view.
    /// </summary>
    public class CameraRigController : MonoBehaviour
    {
        [Header("Limits (degrees, 0 = facing the desk)")]
        [SerializeField] float minYaw = -90f;
        [SerializeField] float maxYaw = 90f;
        [Tooltip("How far you can look up above level.")]
        [SerializeField] float maxLookUp = 45f;
        [Tooltip("How far you can look down below level.")]
        [SerializeField] float maxLookDown = 35f;

        [Header("Mouse")]
        [Tooltip("Degrees of rotation per pixel of mouse movement.")]
        [SerializeField] float mouseSensitivity = 0.15f;
        [Tooltip("Vertical sensitivity relative to horizontal.")]
        [SerializeField] float verticalSensitivity = 0.8f;
        [Tooltip("On: drag the world (drag right/up to look left/down). Off: drag right/up to look right/up.")]
        [SerializeField] bool dragMovesWorld = true;

        [Header("Keyboard (arrow keys)")]
        [SerializeField] float keyboardSpeed = 90f;
        [SerializeField] float keyboardPitchSpeed = 60f;

        [Header("Smoothing")]
        [Tooltip("Seconds to catch up with the target angle. Higher = floatier.")]
        [SerializeField] float smoothTime = 0.18f;

        Transform cameraTransform;
        float defaultPitch;   // the camera's built-in downward tilt (positive = down)
        float targetYaw, currentYaw, yawVelocity;
        float targetPitch, currentPitch, pitchVelocity;

        public float Yaw => currentYaw;

        void Start()
        {
            currentYaw = targetYaw = Mathf.DeltaAngle(0f, transform.localEulerAngles.y);
            var cam = GetComponentInChildren<Camera>();
            if (cam != null)
            {
                cameraTransform = cam.transform;
                defaultPitch = Mathf.DeltaAngle(0f, cameraTransform.localEulerAngles.x);
            }
            currentPitch = targetPitch = defaultPitch;
        }

        public void AddDragDelta(Vector2 pixelDelta)
        {
            float sign = dragMovesWorld ? -1f : 1f;
            targetYaw = Mathf.Clamp(targetYaw + sign * pixelDelta.x * mouseSensitivity, minYaw, maxYaw);
            // Pitch is positive downward in Unity, so screen-up movement maps with the opposite sign.
            SetTargetPitch(targetPitch + sign * -pixelDelta.y * mouseSensitivity * verticalSensitivity);
        }

        /// <summary>Smoothly returns to the default forward view.</summary>
        public void Recenter()
        {
            targetYaw = 0f;
            targetPitch = defaultPitch;
        }

        void SetTargetPitch(float pitch) => targetPitch = Mathf.Clamp(pitch, -maxLookUp, maxLookDown);

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && !InputLock.IsLocked)
            {
                float yawDir = 0f, pitchDir = 0f;
                if (keyboard.leftArrowKey.isPressed) yawDir -= 1f;
                if (keyboard.rightArrowKey.isPressed) yawDir += 1f;
                if (keyboard.upArrowKey.isPressed) pitchDir -= 1f;   // look up
                if (keyboard.downArrowKey.isPressed) pitchDir += 1f; // look down
                targetYaw = Mathf.Clamp(targetYaw + yawDir * keyboardSpeed * Time.deltaTime, minYaw, maxYaw);
                SetTargetPitch(targetPitch + pitchDir * keyboardPitchSpeed * Time.deltaTime);
                if (keyboard.homeKey.wasPressedThisFrame) Recenter();
            }

            currentYaw = Mathf.SmoothDamp(currentYaw, targetYaw, ref yawVelocity, smoothTime);
            transform.localRotation = Quaternion.Euler(0f, currentYaw, 0f);

            currentPitch = Mathf.SmoothDamp(currentPitch, targetPitch, ref pitchVelocity, smoothTime);
            if (cameraTransform != null) cameraTransform.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
        }
    }
}
