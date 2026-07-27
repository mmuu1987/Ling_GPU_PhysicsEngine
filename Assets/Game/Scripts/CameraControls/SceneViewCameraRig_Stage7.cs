using UnityEngine;

public sealed class SceneViewCameraRig_Stage7
{
    private Camera _camera;
    private Transform _point;
    private LocalRotationAndScale_Stage7 _mouseOrbit;

    private bool _orbiting;
    private bool _panning;
    private bool _altRightZooming;
    private bool _freeLooking;
    private Vector3 _lastMousePosition;
    private float _distance = 5f;
    private float _freeLookX;
    private float _freeLookY;

    public void Initialize(Camera camera, Transform point, LocalRotationAndScale_Stage7 mouseOrbit, Transform target)
    {
        _camera = camera;
        _point = point;
        _mouseOrbit = mouseOrbit;

        _point.position = target != null
            ? SceneViewCameraBoundsUtility_Stage7.CalculateBounds(target).center
            : _camera.transform.position + _camera.transform.forward * _distance;

        _mouseOrbit.Target = _point;
        _mouseOrbit.Distance = _distance;
        _mouseOrbit.enabled = false;
        SyncFreeLookAngles();
    }

    public void Tick(SceneViewCameraInput_Stage7 input, SceneViewCameraSettings_Stage7 settings, Transform focusTarget)
    {
        if (_camera == null)
            return;

        if (input.AnyMouseButtonUp)
        {
            StopOrbit();
            _panning = false;
            _altRightZooming = false;
            _freeLooking = false;
            return;
        }

        if (!input.CanUseInput)
            return;

        if (input.FocusPressed)
            Focus(focusTarget);

        if (input.AltPressed && input.RightDown)
        {
            _altRightZooming = true;
            _lastMousePosition = input.MousePosition;
        }

        if (_altRightZooming && input.AltPressed && input.RightHeld)
        {
            AltRightDragZoom(input, settings);
            return;
        }

        if (input.RightDown && !input.AltPressed)
        {
            SyncFreeLookAngles();
            _freeLooking = true;
            _lastMousePosition = input.MousePosition;
        }

        if (input.RightHeld && !input.AltPressed)
        {
            FreeLook(input, settings);
            FlyMove(input, settings);
        }

        if (input.MiddleDown)
        {
            _panning = true;
            _lastMousePosition = input.MousePosition;
        }

        if (_panning && input.MiddleHeld)
            Pan(input, settings);

        if (input.AltPressed && input.LeftDown)
            StartOrbit();

        if (_orbiting && input.AltPressed && input.LeftHeld)
            UpdateOrbitDistanceFromCamera();

        ZoomByMouseWheel(input, settings);
    }

    public void Focus(Transform target)
    {
        if (target == null || _camera == null)
            return;

        Bounds bounds = SceneViewCameraBoundsUtility_Stage7.CalculateBounds(target);
        _point.position = bounds.center;

        float fov = _camera.fieldOfView * Mathf.Deg2Rad;
        float objectSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * 2f;
        _distance = Mathf.Max(objectSize / Mathf.Sin(fov * 0.5f), 0.5f);

        _camera.transform.position = bounds.center - _camera.transform.forward * _distance;
        _camera.transform.LookAt(bounds.center);
        SyncFreeLookAngles();

        if (_mouseOrbit != null)
        {
            _mouseOrbit.Target = _point;
            _mouseOrbit.Distance = _distance;
            _mouseOrbit.RestRotationInfo();
        }
    }

    private void FreeLook(SceneViewCameraInput_Stage7 input, SceneViewCameraSettings_Stage7 settings)
    {
        if (!_freeLooking)
        {
            _freeLooking = true;
            _lastMousePosition = input.MousePosition;
        }

        Vector3 mouseDelta = input.MousePosition - _lastMousePosition;
        float maxDelta = Mathf.Max(settings.MaxFreeLookMouseDeltaPerFrame, 1f);
        mouseDelta.x = Mathf.Clamp(mouseDelta.x, -maxDelta, maxDelta);
        mouseDelta.y = Mathf.Clamp(mouseDelta.y, -maxDelta, maxDelta);

        _freeLookX += mouseDelta.x * settings.FreeLookSensitivity * 0.1f;
        _freeLookY -= mouseDelta.y * settings.FreeLookSensitivity * 0.1f;
        _freeLookY = LocalRotationAndScale_Stage7.ClampAngle(_freeLookY, -89f, 89f);
        _camera.transform.rotation = Quaternion.Euler(_freeLookY, _freeLookX, 0f);
        _point.position = _camera.transform.position + _camera.transform.forward * _distance;
        _lastMousePosition = input.MousePosition;
    }

    private void FlyMove(SceneViewCameraInput_Stage7 input, SceneViewCameraSettings_Stage7 settings)
    {
        Vector3 move = Vector3.zero;

        if (input.MoveForward)
            move += _camera.transform.forward;
        if (input.MoveBackward)
            move -= _camera.transform.forward;
        if (input.MoveRight)
            move += _camera.transform.right;
        if (input.MoveLeft)
            move -= _camera.transform.right;
        if (input.MoveUp)
            move += Vector3.up;
        if (input.MoveDown)
            move -= Vector3.up;

        if (move.sqrMagnitude <= 0.0001f)
            return;

        float speed = settings.FlyMoveSpeed;
        if (input.ShiftPressed)
            speed *= settings.FlyFastMultiplier;

        Vector3 offset = move.normalized * (speed * Time.deltaTime);
        _camera.transform.position += offset;
        _point.position += offset;
    }

    private void AltRightDragZoom(SceneViewCameraInput_Stage7 input, SceneViewCameraSettings_Stage7 settings)
    {
        Vector3 mouseDelta = input.MousePosition - _lastMousePosition;
        float dragAmount = mouseDelta.x + mouseDelta.y;
        if (Mathf.Abs(dragAmount) > 0.001f)
        {
            float scaledDistance = Mathf.Max(_distance, 1f);
            Vector3 offset = _camera.transform.forward * (dragAmount * settings.AltRightDragZoomSensitivity * scaledDistance);
            _camera.transform.position += offset;
            _distance = Mathf.Max(Vector3.Distance(_camera.transform.position, _point.position), 0.01f);

            if (_mouseOrbit != null)
                _mouseOrbit.Distance = _distance;
        }

        _lastMousePosition = input.MousePosition;
    }

    private void Pan(SceneViewCameraInput_Stage7 input, SceneViewCameraSettings_Stage7 settings)
    {
        Vector3 delta = input.MousePosition - _lastMousePosition;
        Vector3 move =
            -_camera.transform.right * (delta.x * settings.PanSensitivity * _distance) -
            _camera.transform.up * (delta.y * settings.PanSensitivity * _distance);

        _camera.transform.position += move;
        _point.position += move;
        _lastMousePosition = input.MousePosition;
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
        _distance = Mathf.Max(Vector3.Distance(_camera.transform.position, _point.position), 0.01f);
        _mouseOrbit.Distance = _distance;
    }

    private void ZoomByMouseWheel(SceneViewCameraInput_Stage7 input, SceneViewCameraSettings_Stage7 settings)
    {
        if (Mathf.Approximately(input.MouseWheel, 0f))
            return;

        Vector3 offset = _camera.transform.forward * (input.MouseWheel * settings.ZoomSensitivity * Mathf.Max(_distance, 1f));
        _camera.transform.position += offset;
        _distance = Mathf.Max(Vector3.Distance(_camera.transform.position, _point.position), 0.01f);

        if (_mouseOrbit != null)
            _mouseOrbit.Distance = _distance;
    }

    private void SyncFreeLookAngles()
    {
        if (_camera == null)
            return;

        Vector3 angles = _camera.transform.eulerAngles;
        _freeLookX = angles.y;
        _freeLookY = NormalizeSignedAngle(angles.x);
    }

    private static float NormalizeSignedAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f)
            angle -= 360f;
        else if (angle < -180f)
            angle += 360f;

        return angle;
    }
}

