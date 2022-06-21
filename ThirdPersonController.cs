using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Cinemachine;

namespace StarterAssets
{
	[RequireComponent(typeof(CharacterController))]
	[RequireComponent(typeof(PlayerInput))]

	public class ThirdPersonController : MonoBehaviour
	{
		#region Player
		[Header("Player Movement")]
		[Tooltip("Move speed of the character in m/s")]
		[SerializeField] private float walkSpeed;
		[Tooltip("Sprint speed of the character in m/s")]
		[SerializeField] private float sprintSpeed;
		[Tooltip("How fast the character turns to face movement direction")]
		[Range(0.0f, 0.3f)]
		[SerializeField] private float rotationSmoothTime = 0.12f;
		[Tooltip("Acceleration and deceleration")]
		[SerializeField] private float speedChangeRate = 10.0f;
		private float sensitivity = 1f;

		[Header("Jump and Gravity")]
		
		[Tooltip("The height the player can jump")]
		[SerializeField] private float jumpHeight;
		[Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
		[SerializeField] private float gravity;
		[Tooltip("The maximum velocity the character will accelerate to")]
		[SerializeField] private float terminalVelocity;
		[Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
		[SerializeField] private float jumpCooldown = 0.50f;
		[Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
		[SerializeField] private float fallTimeout = 0.15f;
		[SerializeField] private bool doubleJump = false;
		[Tooltip("Time required to pass before double jumping")]
		[SerializeField] private float doubleJumpTimeout = 0.1f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		[SerializeField] private bool grounded = true;
		[Tooltip("Useful for rough ground")]
		[SerializeField] private float groundedOffset = -0.14f;
		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		[SerializeField] private float groundedRadius = 0.28f;
		[Tooltip("The layers the character will use as ground")]
		[SerializeField] private LayerMask groundLayers;

		//Player Input Values
		[SerializeField] private Vector2 movement;
		[SerializeField] private Vector2 look;
		[SerializeField] private bool jump;
		[SerializeField] private bool sprint;
		[SerializeField] private bool aim;
		[SerializeField] private bool shoot;

		//Player State Variables
		private float speed;
		private float animationBlend;
		private float targetRotation = 0.0f;
		private float rotationVelocity;
		private float verticalVelocity;
		#endregion

		#region Cinemachine
		[Header("Cinemachine")]
		[SerializeField] private GameObject mainCamera;
		[SerializeField] private CinemachineVirtualCamera aimCamera;
		[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
		[SerializeField] private GameObject cameraPivot;
		[Tooltip("How far in degrees can you move the camera up")]
		[SerializeField] private float topClamp = 70.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		[SerializeField] private float bottomClamp = -30.0f;
		[Tooltip("Additional degress to override the camera. Useful for fine tuning camera position when locked")]
		[SerializeField] private float cameraAngleOverride = 0.0f;
		[Tooltip("For locking the camera position on all axis")]
		[SerializeField] private bool lockCameraPosition = false;

		private float cinemachineTargetYaw;
		private float cinemachineTargetPitch;

		[Header("Player Aiming Settings")]
		[SerializeField] private Image crosshair;
		[SerializeField] private float normalSensitivity;
		[SerializeField] private float aimSensitivity;
		[SerializeField] private float rotationSpeed;
		[SerializeField] private LayerMask aimLayerMask = new LayerMask();

		private Vector3 mouseWorldPosition = Vector3.zero;

		[Header("Shooting and Projectile")]
		[SerializeField] private Transform projectilePrefab;
		[SerializeField] private Transform projectileSpawnPoint;
		[SerializeField] private Transform debugTransform;
		#endregion

		[Header("Player Input Settings")]
		[SerializeField] private bool analogMovement;

		[Header("Mouse Cursor Settings")]
		[SerializeField] private bool cursorLocked = true;
		[SerializeField] private bool cursorInputForLook = true;

		#region Timeout deltatimes
		private float jumpCooldownDelta;
		private float doubleJumpDelta;
		private float fallTimeoutDelta;
		#endregion

		#region Animation Value IDs
		private int animIDSpeed;
		private int animIDGrounded;
		private int animIDJump;
		private int animIDFreeFall;
		private int animIDMotionSpeed;
		#endregion

		#region References
		private Animator animator;
		private CharacterController controller;
		//private InputHandler inputHandler;
		private const float threshold = 0.01f;
		private bool hasAnimator;
		#endregion

		#region Input References
		private PlayerInput playerInput;
		private InputAction moveAction = new InputAction();
		private InputAction lookAction = new InputAction();
		private InputAction jumpAction = new InputAction();
		private InputAction sprintAction = new InputAction();
		private InputAction aimAction = new InputAction();
		private InputAction shootAction = new InputAction();
		#endregion

		private void Awake()
		{
			//If there is no reference to he main camera in the inspector, get a reference to our main camera by searching the scene.
			if (mainCamera == null)
			{
				mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}

			playerInput = GetComponent<PlayerInput>();

			//Get input actions from the input actions asset and subscrive to the necesary events
			moveAction = playerInput.actions["Move"];
			moveAction.performed += context => movement = context.ReadValue<Vector2>();

			lookAction = playerInput.actions["Look"];
			lookAction.performed += context => look = context.ReadValue<Vector2>();

			jumpAction = playerInput.actions["Jump"];

			sprintAction = playerInput.actions["Sprint"];
			sprintAction.performed += context => sprint = true;
			sprintAction.canceled += context => sprint = false;

			aimAction = playerInput.actions["Aim"];
			aimAction.performed += context => aim = true;
			aimAction.canceled += context => aim = false;

			shootAction = playerInput.actions["Shoot"];
			shootAction.performed += context => shoot = context.started;

			//Enable input bindings
			moveAction.Enable();
			lookAction.Enable();
			jumpAction.Enable();
			sprintAction.Enable();
			aimAction.Enable();
			shootAction.Enable();
		}

		private void OnDisable()
		{
			//Disable input bindings to prevent errors
			moveAction.Disable();
			lookAction.Disable();
			jumpAction.Disable();
			sprintAction.Disable();
			aimAction.Disable();
			shootAction.Disable();
		}

		//descard input handler class

		private void Start()
		{
			hasAnimator = TryGetComponent(out animator);
			controller = GetComponent<CharacterController>();

			if (cursorLocked)
            {
				Cursor.lockState = CursorLockMode.Locked;
            }

			AssignAnimationIDs();

			//Reset timeouts on start
			jumpCooldownDelta = jumpCooldown;
			fallTimeoutDelta = fallTimeout;
		}

		private void Update()
		{
			hasAnimator = TryGetComponent(out animator);

			Move();
			JumpAndGravity();
			GroundedCheck();
		}

		private void LateUpdate()
		{
			CameraRotation();
		}

		#region Camera
		private void CameraRotation()
		{
			//Get the center of the screen and proyect a sphere on its postition
			Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);

            #region Debug
            Ray ray = Camera.main.ScreenPointToRay(screenCenterPoint);

			if (Physics.Raycast(ray, out RaycastHit raycast, Mathf.Infinity, aimLayerMask))
			{
				debugTransform.position = raycast.point;
				mouseWorldPosition = raycast.point;
			}
			#endregion

			//Check for aim
			if (aim)
			{
				SetSensitivity(aimSensitivity);
				aimCamera.gameObject.SetActive(true);
				crosshair.gameObject.SetActive(true);

				Vector3 worldAimTarget = mouseWorldPosition;
				worldAimTarget.y = transform.position.y;
				Vector3 aimDirection = (worldAimTarget - transform.position).normalized;

				transform.forward = Vector3.Lerp(transform.forward, aimDirection, Time.deltaTime * rotationSpeed);

				if (shoot)
                {
					Shoot();
                }
			}
			else
			{
				SetSensitivity(normalSensitivity);
				aimCamera.gameObject.SetActive(false);
				crosshair.gameObject.SetActive(false);
			}

			//Rotate camera position if its not fixed.
			if (look.sqrMagnitude >= threshold && !lockCameraPosition)
			{
				cinemachineTargetYaw += look.x * Time.deltaTime * sensitivity;
				cinemachineTargetPitch += look.y * Time.deltaTime * sensitivity;
			}

			//Clamp rotation values (Rotations are clamped so our values are limited 360 degrees)
			cinemachineTargetYaw = ClampAngle(cinemachineTargetYaw, float.MinValue, float.MaxValue);
			cinemachineTargetPitch = ClampAngle(cinemachineTargetPitch, bottomClamp, topClamp);

			//Rotate the pivot object. Cinemachine will follow this object.
			cameraPivot.transform.rotation = Quaternion.Euler(cinemachineTargetPitch + cameraAngleOverride, cinemachineTargetYaw, 0.0f);
		}

		//Set the camera sensivity that will be in use. Either normal sensitivity or aiming sensitivity.
		public void SetSensitivity(float newSensitivty)
		{
			sensitivity = newSensitivty;
		}

		private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}
		#endregion

		#region Movement
		private void Move()
		{
			//Set target speed based on the player input
			float targetSpeed = sprint ? sprintSpeed : walkSpeed;

			//Acceleration and deceleration method.

			//If there is no input, set the target speed to 0
			if (movement == Vector2.zero)
			{
				targetSpeed = 0.0f;
			}

			//Reference to the players current horizontal velocity
			float currentHorizontalSpeed = new Vector3(controller.velocity.x, 0.0f, controller.velocity.z).magnitude;

			float speedOffset = 0.1f;
			//Magnitude of the current input device. If Gamepad is being used, the magnitude of the input will be determined by how far the joystick is pushed
			float inputMagnitude = analogMovement ? movement.magnitude : 1f;

			//Accelerate or decelerate to target speed
			if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
			{
				//Creates curved result rather than a linear one giving a more organic speed change
				speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * speedChangeRate);

				//Round speed to 3 decimal places
				speed = Mathf.Round(speed * 1000f) / 1000f;
			}
			else
			{
				speed = targetSpeed;
			}

			animationBlend = Mathf.Lerp(animationBlend, targetSpeed, Time.deltaTime * speedChangeRate);

			//Get player input and normalise direction
			Vector3 inputDirection = new Vector3(movement.x, 0.0f, movement.y).normalized;

			//If the player moves, rotate player relative to the camera
			if (movement != Vector2.zero)
			{
				targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + mainCamera.transform.eulerAngles.y;
				float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetRotation, ref rotationVelocity, rotationSmoothTime);

				//Rotate to face input direction relative to camera position
				if (!aim)
				{
					transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
				}
			}

			Vector3 targetDirection = Quaternion.Euler(0.0f, targetRotation, 0.0f) * Vector3.forward;

			//Move the player
			controller.Move(targetDirection.normalized * (speed * Time.deltaTime) + new Vector3(0.0f, verticalVelocity, 0.0f) * Time.deltaTime);

			//Update animator if using a character with animator component
			if (hasAnimator)
			{
				animator.SetFloat(animIDSpeed, animationBlend);
				animator.SetFloat(animIDMotionSpeed, inputMagnitude);
			}
		}
		#endregion

		#region Jump and Gravity
		private void JumpAndGravity()
		{
			if (grounded)
			{
				//Reset the fall timeout and double jump timers
				fallTimeoutDelta = fallTimeout;
				doubleJumpDelta = doubleJumpTimeout;

				//Reset double jump
				doubleJump = true;

				//Update animator if using a character with animator component
				if (hasAnimator)
				{
					animator.SetBool(animIDJump, false);
					animator.SetBool(animIDFreeFall, false);
				}

				//If the player is grounded, keep a constant negative vertical velocity
				if (verticalVelocity < 0.0f)
				{
					verticalVelocity = -2f;
				}

				//Jump
				if (jumpAction.triggered && jumpCooldownDelta <= 0.0f)
				{
					//Math operation to get how much velocity is needed to reach desired height (Sqrt(Height * -2 * Gravity))
					verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

					//Update animator if using a character with animator component
					if (hasAnimator)
					{
						animator.SetBool(animIDJump, true);
					}
				}

				//Jump cooldown
				if (jumpCooldownDelta >= 0.0f)
				{
					jumpCooldownDelta -= Time.deltaTime;
				}
			}
            #region Double Jump
            else
            {
				//Double jump
				if (jumpAction.triggered && doubleJumpDelta <= 0.0f && doubleJump)
				{
					//Math operation to get how much velocity is needed to reach desired height (Sqrt(Height * -2 * Gravity))
					verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

					//Update animator if using a character with animator component
					if (hasAnimator)
					{
						animator.Play("Flip");
					}

					//If the player is not grounded, it cannnot jump
					doubleJump = false;
				}
				else
				{
					//Reset the jump cooldown timer
					jumpCooldownDelta = jumpCooldown;

					//Fall timeout
					if (fallTimeoutDelta >= 0.0f)
					{
						fallTimeoutDelta -= Time.deltaTime;
					}
					else
					{
						//Update animator if using a character with animator component
						if (hasAnimator)
						{
							animator.SetBool(animIDFreeFall, true);
						}
					}
				}

				//Double jump timeout
				if (doubleJumpDelta >= 0.0f)
				{
					doubleJumpDelta -= Time.deltaTime;
				}
			}
            #endregion
            //Apply gravity over time if player's velocity is under terminal velocity (multiply by delta time twice to linearly speed up over time)
            if (verticalVelocity < terminalVelocity)
			{
				verticalVelocity += gravity * Time.deltaTime;
			}
		}

		private void GroundedCheck()
		{
			//Set a sphere position, with offset to check if the character is grounded.
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - groundedOffset, transform.position.z);
			grounded = Physics.CheckSphere(spherePosition, groundedRadius, groundLayers, QueryTriggerInteraction.Ignore);

			//If character has an animator, set animator grounded bolean.
			if (hasAnimator)
			{
				animator.SetBool(animIDGrounded, grounded);
			}
		}
		#endregion

		private void Shoot()
        {
			//Shoot projectile and look to aim direction
			Vector3 projectieDirecton = (mouseWorldPosition - projectileSpawnPoint.position).normalized;
			Instantiate(projectilePrefab, projectileSpawnPoint.position, Quaternion.LookRotation(projectieDirecton, Vector3.zero));
		}
		private void AssignAnimationIDs()
		{
			animIDSpeed = Animator.StringToHash("Speed");
			animIDGrounded = Animator.StringToHash("Grounded");
			animIDJump = Animator.StringToHash("Jump");
			animIDFreeFall = Animator.StringToHash("FreeFall");
			animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;

			// when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - groundedOffset, transform.position.z), groundedRadius);
		}
	}
}