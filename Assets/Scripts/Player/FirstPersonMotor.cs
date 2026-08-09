using UnityEngine;
using UnityEngine.InputSystem;

namespace Pho.Player
{
    /// <summary>
    /// Standard first-person locomotion: WASD move relative to body yaw,
    /// mouse look split into body yaw (this transform) + camera pitch
    /// (cameraPivot), driven by CharacterController. Deliberately its own
    /// component, separate from PlayerInteractor -- composition over one
    /// PlayerController god-class, per the project's stated engineering
    /// principles (architecture.md's design philosophy).
    ///
    /// Speeds are the frozen M2 acceptance-criteria numbers: walk 3.2 m/s,
    /// sprint 5.0 m/s. Jump is not called out as in-scope anywhere in
    /// architecture.md §9/§10 for M2 and is skipped.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonMotor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] CharacterController controller;
        [Tooltip("Pitches with mouse-look Y. Should be (or be the parent of) the Camera used by PlayerInteractor. Yaw is applied to this GameObject's own transform instead.")]
        [SerializeField] Transform cameraPivot;

        [Header("Move")]
        [SerializeField] float walkSpeed = 3.2f;
        [SerializeField] float sprintSpeed = 5.0f;

        [Header("Look")]
        [Tooltip("Degrees applied per raw pointer-delta unit from <Mouse>/delta. Tune once this can be playtested.")]
        [SerializeField] float mouseSensitivity = 0.12f;
        [SerializeField] float minPitch = -80f;
        [SerializeField] float maxPitch = 80f;

        [Header("Gravity")]
        [Tooltip("Keeps CharacterController.isGrounded honest on uneven floors even though jump is out of scope for M2.")]
        [SerializeField] float gravity = -9.81f;
        [SerializeField] float groundedStickVelocity = -2f;

        [Header("Cursor")]
        [SerializeField] bool lockCursorOnEnable = true;

        InputAction _moveAction;
        InputAction _lookAction;
        InputAction _sprintAction;

        float _pitch;
        float _verticalVelocity;

        void Awake()
        {
            if (controller == null) controller = GetComponent<CharacterController>();

            _moveAction = new InputAction(name: "Move", type: InputActionType.Value, expectedControlType: "Vector2");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            _lookAction = new InputAction(name: "Look", type: InputActionType.Value, binding: "<Mouse>/delta");
            _sprintAction = new InputAction(name: "Sprint", type: InputActionType.Button, binding: "<Keyboard>/leftShift");
        }

        void OnEnable()
        {
            _moveAction.Enable();
            _lookAction.Enable();
            _sprintAction.Enable();

            if (lockCursorOnEnable)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void OnDisable()
        {
            _moveAction.Disable();
            _lookAction.Disable();
            _sprintAction.Disable();
        }

        void OnDestroy()
        {
            _moveAction?.Dispose();
            _lookAction?.Dispose();
            _sprintAction?.Dispose();
        }

        void Update()
        {
            ApplyLook();
            ApplyMove();
        }

        void ApplyLook()
        {
            var delta = _lookAction.ReadValue<Vector2>();
            if (delta.sqrMagnitude <= 0f) return;

            float yawDelta = delta.x * mouseSensitivity;
            float pitchDelta = -delta.y * mouseSensitivity;

            transform.Rotate(Vector3.up, yawDelta, Space.World);

            _pitch = Mathf.Clamp(_pitch + pitchDelta, minPitch, maxPitch);
            if (cameraPivot != null)
                cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void ApplyMove()
        {
            var input = _moveAction.ReadValue<Vector2>();
            var localMove = new Vector3(input.x, 0f, input.y);
            if (localMove.sqrMagnitude > 1f) localMove.Normalize();

            bool sprinting = _sprintAction.IsPressed();
            float speed = sprinting ? sprintSpeed : walkSpeed;

            var worldMove = transform.TransformDirection(localMove) * speed;

            if (controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = groundedStickVelocity;
            _verticalVelocity += gravity * Time.deltaTime;

            var velocity = worldMove;
            velocity.y = _verticalVelocity;

            controller.Move(velocity * Time.deltaTime);
        }
    }
}
