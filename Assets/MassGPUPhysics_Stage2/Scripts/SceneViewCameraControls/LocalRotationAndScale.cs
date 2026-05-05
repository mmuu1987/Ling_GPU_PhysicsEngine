using UnityEngine;

/// <summary>
/// Orbit camera rotation around a target point, with a fixed camera distance.
/// Adapted from https://blog.csdn.net/mmuu1987/article/details/85171152
/// Original article by CSDN user mmuu1987, CC BY-SA 4.0 as shown on CSDN.
/// </summary>
public class LocalRotationAndScale : MonoBehaviour
{
    public Transform Target;
    public float Distance = 5f;
    public float XSpeed = 5f;
    public float YSpeed = 5f;
    public float YMinLimit = -360f;
    public float YMaxLimit = 360f;
    public float DistanceMin = 0.5f;
    public float DistanceMax = 5000f;

    private Camera _camera;
    private float _x;
    private float _y;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void Start()
    {
        SyncAngles();
    }

    public void SyncAngles()
    {
        Vector3 angles = transform.eulerAngles;
        _x = angles.y;
        _y = angles.x;
    }

    private void OnEnable()
    {
        RestRotationInfo();
    }

    private void LateUpdate()
    {
        if (Target == null)
            return;

        float deltaX = Input.GetAxis("Mouse X") * XSpeed;
        float deltaY = Input.GetAxis("Mouse Y") * YSpeed;

        _x += deltaX;
        _y -= deltaY;
        _y = ClampAngle(_y, YMinLimit, YMaxLimit);
        Distance = Mathf.Clamp(Distance, DistanceMin, DistanceMax);

        Zoom();
    }

    public void RestRotationInfo()
    {
        Transform cameraTransform = _camera != null ? _camera.transform : transform;
        _y = cameraTransform.localEulerAngles.x;
        _x = cameraTransform.localEulerAngles.y;
    }

    public void Zoom()
    {
        if (Target == null)
            return;

        Quaternion rotation = Quaternion.Euler(_y, _x, 0f);
        transform.rotation = rotation;

        Vector3 negativeDistance = new Vector3(0f, 0f, -Distance);
        transform.position = rotation * negativeDistance + Target.position;
    }

    public static float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360f)
            angle += 360f;
        if (angle > 360f)
            angle -= 360f;

        return Mathf.Clamp(angle, min, max);
    }
}
