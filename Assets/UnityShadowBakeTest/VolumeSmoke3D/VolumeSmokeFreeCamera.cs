using UnityEngine;
using UnityEngine.InputSystem;

namespace TA.VolumeSmoke3D
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class VolumeSmokeFreeCamera : MonoBehaviour
    {
        [Min(0.1f)] public float moveSpeed = 3;
        [Min(1)] public float boostMultiplier = 3;
        [Range(0.01f, 1)] public float lookSensitivity = 0.15f;
        public bool showHelp = true;
        bool looking, blockedUntilRelease;
        CursorLockMode previousLock;
        bool previousVisibility;

        void Update()
        {
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if (mouse == null || keyboard == null) return;
            if (!mouse.rightButton.isPressed)
            {
                blockedUntilRelease = false;
                ReleaseMouse();
                return;
            }
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                blockedUntilRelease = true;
                ReleaseMouse();
                return;
            }
            if (blockedUntilRelease) return;
            bool justCaptured = !looking;
            if (justCaptured)
            {
                previousLock = Cursor.lockState;
                previousVisibility = Cursor.visible;
                looking = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0) moveSpeed = Mathf.Clamp(moveSpeed * Mathf.Exp(scroll * 0.001f), 0.1f, 30);
            Vector3 axes = new Vector3(
                (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.eKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            Vector2 look = justCaptured ? Vector2.zero : mouse.delta.ReadValue();
            bool boost = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            MoveAndLook(look, axes, Time.unscaledDeltaTime, boost ? boostMultiplier : 1);
        }

        public void MoveAndLook(Vector2 lookDelta, Vector3 movementAxes, float deltaTime, float speedMultiplier = 1)
        {
            Vector3 angles = transform.eulerAngles;
            float pitch = angles.x > 180 ? angles.x - 360 : angles.x;
            pitch = Mathf.Clamp(pitch - lookDelta.y * lookSensitivity, -89, 89);
            transform.rotation = Quaternion.Euler(pitch, angles.y + lookDelta.x * lookSensitivity, 0);
            Vector3 axes = Vector3.ClampMagnitude(movementAxes, 1);
            Vector3 direction = transform.right * axes.x + Vector3.up * axes.y + transform.forward * axes.z;
            direction = Vector3.ClampMagnitude(direction, 1);
            transform.position += direction * Mathf.Max(0, deltaTime) * moveSpeed * speedMultiplier;
        }

        void ReleaseMouse()
        {
            if (!looking) return;
            Cursor.lockState = previousLock;
            Cursor.visible = previousVisibility;
            looking = false;
        }
        void OnApplicationFocus(bool focused) { if (!focused) ReleaseMouse(); }
        void OnDisable() => ReleaseMouse();
        void OnDestroy() => ReleaseMouse();
        void OnGUI()
        {
            if (!showHelp) return;
            GUI.Box(new Rect(18, 640, 470, 47),
                $"RMB + mouse: look | WASD: move | Q/E: down/up\nShift: boost | RMB + wheel: speed ({moveSpeed:F1}) | Esc: release mouse");
        }
    }
}
