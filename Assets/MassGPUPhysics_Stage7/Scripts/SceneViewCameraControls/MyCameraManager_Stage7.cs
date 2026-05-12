using UnityEngine;

/// <summary>
/// Runtime Scene View style camera controller.
/// This MonoBehaviour keeps the serialized Unity entry point, while the input,
/// bounds math, and camera rig behavior live in small focused classes.
/// </summary>
public class MyCameraManager_Stage7 : MonoBehaviour
{
    [Header("Target")]
    public Transform Target;

    [Header("Camera")]
    public Camera ControlledCamera;
    public bool CreateCameraIfMissing = true;

    [Header("Input")]
    [Tooltip("When enabled, input only works while the mouse is inside the Game view screen rectangle.")]
    public bool RequireMouseInsideScreen = true;
    [Tooltip("Optional normalized input area. (0,0,1,1) means the full screen.")]
    public Rect NormalizedInputArea = new Rect(0f, 0f, 1f, 1f);
    public float PanSensitivity = 0.01f;
    public float FreeLookSensitivity = 4f;
    public float MaxFreeLookMouseDeltaPerFrame = 80f;
    public float FlyMoveSpeed = 10f;
    public float FlyFastMultiplier = 3f;
    public float ZoomSensitivity = 10f;
    public float AltRightDragZoomSensitivity = 0.01f;

    private bool _lockInput;
    private Transform _point;
    private LocalRotationAndScale_Stage7 _mouseOrbit;
    private readonly SceneViewCameraRig_Stage7 _rig = new SceneViewCameraRig_Stage7();

    public Camera MainCamera => ControlledCamera;

    protected virtual void Start()
    {
        EnsureCamera();
        if (ControlledCamera == null)
        {
            Debug.LogError("[MyCameraManager_Stage7] No camera found. Assign ControlledCamera or enable CreateCameraIfMissing.");
            enabled = false;
            return;
        }

        GameObject pointObject = new GameObject("SceneViewCamera_Point");
        _point = pointObject.transform;
        pointObject.hideFlags = HideFlags.DontSave;

        _mouseOrbit = ControlledCamera.GetComponent<LocalRotationAndScale_Stage7>();
        if (_mouseOrbit == null)
            _mouseOrbit = ControlledCamera.gameObject.AddComponent<LocalRotationAndScale_Stage7>();

        _rig.Initialize(ControlledCamera, _point, _mouseOrbit, Target);
        UnlockInput();
    }

    private void EnsureCamera()
    {
        if (ControlledCamera != null)
            return;

        ControlledCamera = Camera.main;
        if (ControlledCamera != null || !CreateCameraIfMissing)
            return;

        GameObject cameraObject = new GameObject("_mainCamera");
        ControlledCamera = cameraObject.AddComponent<Camera>();
        cameraObject.transform.position = new Vector3(0f, 2f, -5f);
        cameraObject.transform.rotation = Quaternion.Euler(20f, 0f, 0f);
    }

    public void LockInput()
    {
        _lockInput = true;
    }

    public void UnlockInput()
    {
        _lockInput = false;
        if (ControlledCamera != null)
            ControlledCamera.enabled = true;
    }

    private void Update()
    {
        if (ControlledCamera == null || _lockInput)
            return;

        SceneViewCameraSettings_Stage7 settings = BuildSettings();
        SceneViewCameraInput_Stage7 input = SceneViewCameraInputReader_Stage7.Read(settings);
        _rig.Tick(input, settings, Target);
    }

    protected void Focus()
    {
        _rig.Focus(Target);
    }

    public static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
    {
        return SceneViewCameraBoundsUtility_Stage7.TransformBounds(matrix, bounds);
    }

    private SceneViewCameraSettings_Stage7 BuildSettings()
    {
        return new SceneViewCameraSettings_Stage7
        {
            RequireMouseInsideScreen = RequireMouseInsideScreen,
            NormalizedInputArea = NormalizedInputArea,
            PanSensitivity = PanSensitivity,
            FreeLookSensitivity = FreeLookSensitivity,
            MaxFreeLookMouseDeltaPerFrame = MaxFreeLookMouseDeltaPerFrame,
            FlyMoveSpeed = FlyMoveSpeed,
            FlyFastMultiplier = FlyFastMultiplier,
            ZoomSensitivity = ZoomSensitivity,
            AltRightDragZoomSensitivity = AltRightDragZoomSensitivity
        };
    }

    private void OnDestroy()
    {
        if (_point != null)
            Destroy(_point.gameObject);
    }
}

