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

    public Camera MainCamera => ControlledCamera;

    protected virtual void Start()
    {
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
        SyncFreeLookAngles();
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
        HandleInput();
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

        Vector3 offset = move.normalized * (speed * Time.deltaTime);
        ControlledCamera.transform.position += offset;
        _point.position += offset;
    }

    private void AltRightDragZoom()
    {
        Vector3 mouseDelta = Input.mousePosition - _lastMousePosition;
        float dragAmount = mouseDelta.x + mouseDelta.y;
        if (Mathf.Abs(dragAmount) > 0.001f)
        {
            float scaledDistance = Mathf.Max(_distance, 1f);
            Vector3 offset = ControlledCamera.transform.forward * (dragAmount * AltRightDragZoomSensitivity * scaledDistance);
            ControlledCamera.transform.position += offset;
            _distance = Mathf.Max(Vector3.Distance(ControlledCamera.transform.position, _point.position), 0.01f);

            if (_mouseOrbit != null)
                _mouseOrbit.Distance = _distance;
        }

        _lastMousePosition = Input.mousePosition;
    }

    private void Pan()
    {
        Vector3 delta = Input.mousePosition - _lastMousePosition;
        Vector3 move =
            -ControlledCamera.transform.right * (delta.x * PanSensitivity * _distance) -
            ControlledCamera.transform.up * (delta.y * PanSensitivity * _distance);

        ControlledCamera.transform.position += move;
        _point.position += move;
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
        _distance = Mathf.Max(Vector3.Distance(ControlledCamera.transform.position, _point.position), 0.01f);
        _mouseOrbit.Distance = _distance;
    }

    private void ZoomByMouseWheel()
    {
        float mouseWheel = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Approximately(mouseWheel, 0f))
            return;

        Vector3 offset = ControlledCamera.transform.forward * (mouseWheel * ZoomSensitivity * Mathf.Max(_distance, 1f));
        ControlledCamera.transform.position += offset;
        _distance = Mathf.Max(Vector3.Distance(ControlledCamera.transform.position, _point.position), 0.01f);

        if (_mouseOrbit != null)
            _mouseOrbit.Distance = _distance;
    }

    protected void Focus()
    {
        if (Target == null)
            return;

        Bounds bounds = CalculateBounds(Target);
        _point.position = bounds.center;

        float fov = ControlledCamera.fieldOfView * Mathf.Deg2Rad;
        float objectSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * 2f;
        _distance = Mathf.Max(objectSize / Mathf.Sin(fov * 0.5f), 0.5f);

        ControlledCamera.transform.position = bounds.center - ControlledCamera.transform.forward * _distance;
        ControlledCamera.transform.LookAt(bounds.center);
        SyncFreeLookAngles();

        if (_mouseOrbit != null)
        {
            _mouseOrbit.Target = _point;
            _mouseOrbit.Distance = _distance;
            _mouseOrbit.RestRotationInfo();
        }
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
        _freeLookY = angles.x;
    }

    private void OnDestroy()
    {
        if (_point != null)
            Destroy(_point.gameObject);
    }
}
