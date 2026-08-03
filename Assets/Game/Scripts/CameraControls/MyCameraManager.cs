using UnityEngine;

/// <summary>
/// 
/// 
/// 
/// </summary>
public class MyCameraManager : MonoBehaviour
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
    public float FlyMoveSpeed = 10f;
    public float FlyFastMultiplier = 3f;
    public float ZoomSensitivity = 10f;
    public float AltRightDragZoomSensitivity = 0.01f;
    [Min(0.1f)] public float MinZoomDistance = 2f;
    [Min(1f)] public float MaxZoomDistance = 2500f;
    [Min(1f)] public float MaxTranslationPerFrame = 200f;
    [Min(100f)] public float MaxWorldCoordinate = 5000f;
    [Min(1f)] public float MaxMouseDeltaPerFrame = 80f;

    private bool _lockInput;
    private bool _orbiting;
    private bool _panning;
    private bool _altRightZooming;
    private Transform _point;
    private LocalRotationAndScale _mouseOrbit;
    private Vector3 _lastMousePosition;
    private float _distance = 5f;
    private float _freeLookX;
    private float _freeLookY;
    private Vector3 _lastSafeCameraPosition;
    private Vector3 _lastSafePointPosition;
    private bool _reportedInvalidTransform;

    public Camera MainCamera => ControlledCamera;

    protected virtual void Start()
    {
        SanitizeSettings();
        EnsureCamera();
        if (ControlledCamera == null)
        {
            Debug.LogError("[MyCameraManager] No camera found. Assign ControlledCamera or enable CreateCameraIfMissing.");
            enabled = false;
            return;
        }

        GameObject pointObject = new GameObject("SceneViewCamera_Point");
        _point = pointObject.transform;
        pointObject.hideFlags = HideFlags.DontSave;
        _point.position = Target != null
            ? CalculateBounds(Target).center
            : ControlledCamera.transform.position + ControlledCamera.transform.forward * _distance;

        _mouseOrbit = ControlledCamera.GetComponent<LocalRotationAndScale>();
        if (_mouseOrbit == null)
            _mouseOrbit = ControlledCamera.gameObject.AddComponent<LocalRotationAndScale>();

        _mouseOrbit.Target = _point;
        _mouseOrbit.Distance = _distance;
        _mouseOrbit.enabled = false;
        _distance = Mathf.Clamp(
            Vector3.Distance(ControlledCamera.transform.position, _point.position),
            MinZoomDistance,
            MaxZoomDistance);
        _lastSafeCameraPosition = CameraMotionSafety.ClampWorldPosition(
            ControlledCamera.transform.position, MaxWorldCoordinate);
        _lastSafePointPosition = CameraMotionSafety.ClampWorldPosition(_point.position, MaxWorldCoordinate);
        SyncFreeLookAngles();
        UnlockInput();
    }

    private void OnValidate()
    {
        SanitizeSettings();
    }

    private void SanitizeSettings()
    {
        PanSensitivity = Mathf.Max(0f, PanSensitivity);
        FreeLookSensitivity = Mathf.Max(0f, FreeLookSensitivity);
        FlyMoveSpeed = Mathf.Max(0f, FlyMoveSpeed);
        FlyFastMultiplier = Mathf.Clamp(FlyFastMultiplier, 1f, 10f);
        ZoomSensitivity = Mathf.Clamp(ZoomSensitivity, 0f, 20f);
        AltRightDragZoomSensitivity = Mathf.Max(0f, AltRightDragZoomSensitivity);
        MinZoomDistance = Mathf.Max(0.1f, MinZoomDistance);
        MaxZoomDistance = Mathf.Max(MinZoomDistance, MaxZoomDistance);
        MaxTranslationPerFrame = Mathf.Max(1f, MaxTranslationPerFrame);
        MaxWorldCoordinate = Mathf.Max(100f, MaxWorldCoordinate);
        MaxMouseDeltaPerFrame = Mathf.Max(1f, MaxMouseDeltaPerFrame);
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
        RecoverInvalidTransform();
        HandleInput();
    }

    private void LateUpdate()
    {
        RecoverInvalidTransform();
        CaptureSafeTransform();
    }

    private void HandleInput()
    {
        if (ControlledCamera == null || _lockInput)
            return;

        bool canUseInput = IsMouseInsideInputArea();

        if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(2))
        {
            StopOrbit();
            _panning = false;
            _altRightZooming = false;
            return;
        }

        if (!canUseInput)
            return;

        if (Input.GetKeyDown(KeyCode.F))
            Focus();

        bool altPressed = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr);

        if (altPressed && Input.GetMouseButtonDown(1))
        {
            _altRightZooming = true;
            _lastMousePosition = Input.mousePosition;
        }

        if (_altRightZooming && altPressed && Input.GetMouseButton(1))
        {
            AltRightDragZoom();
            return;
        }

        if (Input.GetMouseButtonDown(1) && !altPressed)
            SyncFreeLookAngles();

        if (Input.GetMouseButton(1) && !altPressed)
        {
            FreeLook();
            FlyMove();
        }

        if (Input.GetMouseButtonDown(2))
        {
            _panning = true;
            _lastMousePosition = Input.mousePosition;
        }

        if (_panning && Input.GetMouseButton(2))
            Pan();

        if (altPressed && Input.GetMouseButtonDown(0))
            StartOrbit();

        if (_orbiting && altPressed && Input.GetMouseButton(0))
            UpdateOrbitDistanceFromCamera();

        ZoomByMouseWheel();
    }

    private void FreeLook()
    {
        _freeLookX += Input.GetAxis("Mouse X") * FreeLookSensitivity;
        _freeLookY -= Input.GetAxis("Mouse Y") * FreeLookSensitivity;
        _freeLookY = LocalRotationAndScale.ClampAngle(_freeLookY, -89f, 89f);

        ControlledCamera.transform.rotation = Quaternion.Euler(_freeLookY, _freeLookX, 0f);
        _point.position = ControlledCamera.transform.position + ControlledCamera.transform.forward * _distance;
    }

    private void FlyMove()
    {
        Vector3 move = Vector3.zero;

        if (Input.GetKey(KeyCode.W))
            move += ControlledCamera.transform.forward;
        if (Input.GetKey(KeyCode.S))
            move -= ControlledCamera.transform.forward;
        if (Input.GetKey(KeyCode.D))
            move += ControlledCamera.transform.right;
        if (Input.GetKey(KeyCode.A))
            move -= ControlledCamera.transform.right;
        if (Input.GetKey(KeyCode.E))
            move += Vector3.up;
        if (Input.GetKey(KeyCode.Q))
            move -= Vector3.up;

        if (move.sqrMagnitude <= 0.0001f)
            return;

        float speed = FlyMoveSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            speed *= FlyFastMultiplier;

        float safeDeltaTime = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
        Vector3 offset = move.normalized * (Mathf.Clamp(speed, 0f, 500f) * safeDeltaTime);
        ApplyTranslation(offset, true);
    }

    private void AltRightDragZoom()
    {
        Vector3 mouseDelta = ClampMouseDelta(Input.mousePosition - _lastMousePosition);
        float dragAmount = mouseDelta.x + mouseDelta.y;
        if (Mathf.Abs(dragAmount) > 0.001f)
        {
            float scaledDistance = Mathf.Max(_distance, 1f);
            Vector3 offset = ControlledCamera.transform.forward * (dragAmount * AltRightDragZoomSensitivity * scaledDistance);
            ApplyTranslation(offset, false);
            UpdateOrbitDistanceFromCamera();

            if (_mouseOrbit != null)
                _mouseOrbit.Distance = _distance;
        }

        _lastMousePosition = Input.mousePosition;
    }

    private void Pan()
    {
        Vector3 delta = ClampMouseDelta(Input.mousePosition - _lastMousePosition);
        Vector3 move =
            -ControlledCamera.transform.right * (delta.x * PanSensitivity * _distance) -
            ControlledCamera.transform.up * (delta.y * PanSensitivity * _distance);

        ApplyTranslation(move, true);
        _lastMousePosition = Input.mousePosition;
    }

    private void StartOrbit()
    {
        _orbiting = true;
        _mouseOrbit.Target = _point;
        _mouseOrbit.Distance = _distance;
        _mouseOrbit.RestRotationInfo();
        _mouseOrbit.enabled = true;
    }

    private void StopOrbit()
    {
        _orbiting = false;
        if (_mouseOrbit != null)
            _mouseOrbit.enabled = false;
        SyncFreeLookAngles();
    }

    private void UpdateOrbitDistanceFromCamera()
    {
        _distance = Mathf.Clamp(
            Vector3.Distance(ControlledCamera.transform.position, _point.position),
            MinZoomDistance,
            MaxZoomDistance);
        _mouseOrbit.Distance = _distance;
    }

    private void ZoomByMouseWheel()
    {
        float mouseWheel = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Approximately(mouseWheel, 0f))
            return;

        float newDistance = CameraMotionSafety.ResolveZoomDistance(
            _distance, mouseWheel, ZoomSensitivity, MinZoomDistance, MaxZoomDistance);
        Vector3 nextPosition = _point.position - ControlledCamera.transform.forward * newDistance;
        ControlledCamera.transform.position = CameraMotionSafety.ClampWorldPosition(nextPosition, MaxWorldCoordinate);
        _distance = newDistance;

        if (_mouseOrbit != null)
            _mouseOrbit.Distance = _distance;
    }

    protected void Focus()
    {
        if (Target == null)
            return;

        FocusBounds(CalculateBounds(Target));
    }

    public void FocusBounds(Bounds bounds)
    {
        if (ControlledCamera == null || _point == null)
            return;

        Vector3 center = CameraMotionSafety.ClampWorldPosition(bounds.center, MaxWorldCoordinate);
        _point.position = center;

        float fov = ControlledCamera.fieldOfView * Mathf.Deg2Rad;
        float objectSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * 2f;
        float sine = Mathf.Max(0.01f, Mathf.Sin(Mathf.Clamp(fov * 0.5f, 0.01f, 1.5f)));
        _distance = Mathf.Clamp(objectSize / sine, MinZoomDistance, MaxZoomDistance);

        ControlledCamera.transform.position = CameraMotionSafety.ClampWorldPosition(
            center - ControlledCamera.transform.forward * _distance,
            MaxWorldCoordinate);
        ControlledCamera.transform.LookAt(center);
        ControlledCamera.farClipPlane = Mathf.Clamp(
            Mathf.Max(ControlledCamera.farClipPlane, _distance + objectSize),
            100f,
            MaxWorldCoordinate * 2f);
        SyncFreeLookAngles();

        if (_mouseOrbit != null)
        {
            _mouseOrbit.Target = _point;
            _mouseOrbit.Distance = _distance;
            _mouseOrbit.RestRotationInfo();
        }

        CaptureSafeTransform();
    }

    public void FocusTacticalBounds(Bounds bounds)
    {
        if (ControlledCamera == null)
            return;

        ControlledCamera.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        FocusBounds(bounds);
    }

    /// <summary>
    /// Smoothly translates the current tactical framing with a moving GPU population.
    /// Distance and viewing angle stay unchanged, avoiding zoom pumping as formations
    /// briefly stretch or lose outlying units.
    /// </summary>
    public void FollowTacticalBounds(Bounds bounds, float sharpness)
    {
        if (ControlledCamera == null || _point == null || !CameraMotionSafety.IsFinite(bounds.center))
            return;

        Vector3 target = CameraMotionSafety.ClampWorldPosition(bounds.center, MaxWorldCoordinate);
        Vector3 step = CameraMotionSafety.ResolveFollowStep(
            _point.position,
            target,
            sharpness,
            Time.unscaledDeltaTime,
            MaxTranslationPerFrame);
        ApplyTranslation(step, true);
    }

    public void CenterTacticalPoint(Vector3 point)
    {
        if (ControlledCamera == null || _point == null || !CameraMotionSafety.IsFinite(point))
            return;

        Vector3 target = CameraMotionSafety.ClampWorldPosition(point, MaxWorldCoordinate);
        Vector3 offset = target - _point.position;
        ControlledCamera.transform.position = CameraMotionSafety.ClampWorldPosition(
            ControlledCamera.transform.position + offset,
            MaxWorldCoordinate);
        _point.position = target;
        CaptureSafeTransform();
    }

    private bool IsMouseInsideInputArea()
    {
        if (!RequireMouseInsideScreen)
            return true;

        Vector3 mouse = Input.mousePosition;
        Rect area = new Rect(
            NormalizedInputArea.x * Screen.width,
            NormalizedInputArea.y * Screen.height,
            NormalizedInputArea.width * Screen.width,
            NormalizedInputArea.height * Screen.height);

        return area.Contains(mouse);
    }

    private Bounds CalculateBounds(Transform target)
    {
        Renderer renderer = target.GetComponent<Renderer>();
        Bounds bounds = renderer != null
            ? renderer.bounds
            : new Bounds(target.position, Vector3.one);

        CalculateChildBounds(target, ref bounds);

        if (bounds.extents == Vector3.zero)
            bounds.extents = new Vector3(0.5f, 0.5f, 0.5f);

        return bounds;
    }

    private void CalculateChildBounds(Transform target, ref Bounds totalBounds)
    {
        foreach (Transform child in target)
        {
            Renderer renderer = child.GetComponent<Renderer>();
            if (renderer != null)
            {
                totalBounds.Encapsulate(renderer.bounds.min);
                totalBounds.Encapsulate(renderer.bounds.max);
            }

            CalculateChildBounds(child, ref totalBounds);
        }
    }

    public static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
    {
        Vector3 center = matrix.MultiplyPoint(bounds.center);
        Vector3 extents = bounds.extents;
        Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));

        extents.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
        extents.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
        extents.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

        return new Bounds(center, extents * 2f);
    }

    private void SyncFreeLookAngles()
    {
        if (ControlledCamera == null)
            return;

        Vector3 angles = ControlledCamera.transform.eulerAngles;
        _freeLookX = angles.y;
        _freeLookY = CameraMotionSafety.NormalizeSignedAngle(angles.x);
    }

    private Vector3 ClampMouseDelta(Vector3 delta)
    {
        delta.x = Mathf.Clamp(delta.x, -MaxMouseDeltaPerFrame, MaxMouseDeltaPerFrame);
        delta.y = Mathf.Clamp(delta.y, -MaxMouseDeltaPerFrame, MaxMouseDeltaPerFrame);
        return delta;
    }

    private void ApplyTranslation(Vector3 offset, bool movePoint)
    {
        offset = CameraMotionSafety.ClampStep(offset, MaxTranslationPerFrame);
        ControlledCamera.transform.position = CameraMotionSafety.ClampWorldPosition(
            ControlledCamera.transform.position + offset,
            MaxWorldCoordinate);
        if (movePoint)
        {
            _point.position = CameraMotionSafety.ClampWorldPosition(
                _point.position + offset,
                MaxWorldCoordinate);
        }
    }

    private void RecoverInvalidTransform()
    {
        if (ControlledCamera == null || _point == null)
            return;

        bool cameraValid = CameraMotionSafety.IsFinite(ControlledCamera.transform.position);
        bool pointValid = CameraMotionSafety.IsFinite(_point.position);
        bool distanceValid = CameraMotionSafety.IsFinite(_distance);
        if (cameraValid && pointValid && distanceValid)
            return;

        ControlledCamera.transform.position = CameraMotionSafety.IsFinite(_lastSafeCameraPosition)
            ? _lastSafeCameraPosition
            : Vector3.zero;
        _point.position = CameraMotionSafety.IsFinite(_lastSafePointPosition)
            ? _lastSafePointPosition
            : ControlledCamera.transform.position + ControlledCamera.transform.forward * MinZoomDistance;
        _distance = Mathf.Clamp(
            Vector3.Distance(ControlledCamera.transform.position, _point.position),
            MinZoomDistance,
            MaxZoomDistance);

        if (!_reportedInvalidTransform)
        {
            _reportedInvalidTransform = true;
            Debug.LogWarning("[MyCameraManager] Invalid camera transform was rejected and restored to the last safe position.", this);
        }
    }

    private void CaptureSafeTransform()
    {
        if (ControlledCamera == null || _point == null ||
            !CameraMotionSafety.IsFinite(ControlledCamera.transform.position) ||
            !CameraMotionSafety.IsFinite(_point.position))
            return;

        _lastSafeCameraPosition = ControlledCamera.transform.position;
        _lastSafePointPosition = _point.position;
        _reportedInvalidTransform = false;
    }

    private void OnDestroy()
    {
        if (_point != null)
            Destroy(_point.gameObject);
    }
}
