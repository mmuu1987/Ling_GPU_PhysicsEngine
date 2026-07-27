using UnityEngine;

/// <summary>
/// 轨道摄像机旋转组件：让摄像机围绕一个目标点（Target）做球面轨道旋转，保持固定距离。
///
/// 【轨道摄像机的工作原理】
/// 想象摄像机被拴在一个看不见的球壳上，球壳的中心是 Target（焦点）。
/// 摄像机永远面向 Target，鼠标拖拽改变的是摄像机在球壳上的经度(_x)和纬度(_y)位置。
/// Distance 是球壳的半径——摄像机离 Target 有多远。
///
/// 【与 MyCameraManager_Stage5 的关系】
/// MyCameraManager 负责处理输入逻辑（判断哪个按键、哪种操作模式），
/// 本组件只负责纯粹的"轨道旋转 + 缩放"数学计算。
/// MyCameraManager 在需要轨道模式时启用本组件（enabled=true），其他时候禁用。
///
/// 【坐标系说明】
/// - _x（经度/Yaw）：绕世界 Y 轴旋转，控制水平视角。正值=向右转。
/// - _y（纬度/Pitch）：绕世界 X 轴旋转，控制垂直视角。正值=向上看。
/// - Distance：摄像机到 Target 的距离，滚轮控制。
///
/// 改编自 CSDN 用户 mmuu1987 的文章（CC BY-SA 4.0）
/// https://blog.csdn.net/mmuu1987/article/details/85171152
/// </summary>
public class LocalRotationAndScale_Stage5 : MonoBehaviour
{
    /// <summary>轨道旋转的中心点（摄像机始终面朝这个点）</summary>
    public Transform Target;

    /// <summary>摄像机到 Target 的距离（球壳半径）</summary>
    public float Distance = 5f;

    /// <summary>鼠标 X 方向移动的旋转灵敏度（经度/Yaw）</summary>
    public float XSpeed = 5f;

    /// <summary>鼠标 Y 方向移动的旋转灵敏度（纬度/Pitch）</summary>
    public float YSpeed = 5f;

    /// <summary>垂直视角下限（度），默认 -360 表示无限制</summary>
    public float YMinLimit = -360f;

    /// <summary>垂直视角上限（度），默认 360 表示无限制</summary>
    public float YMaxLimit = 360f;

    /// <summary>距离 Target 的最小值，防止摄像机穿入物体内部</summary>
    public float DistanceMin = 0.5f;

    /// <summary>距离 Target 的最大值</summary>
    public float DistanceMax = 5000f;

    private Camera _camera;
    private float _x; // 当前经度/Yaw 角度（度）
    private float _y; // 当前纬度/Pitch 角度（度）

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void Start()
    {
        SyncAngles();
    }

    /// <summary>
    /// 从当前 Transform 的欧拉角同步 _x/_y 变量。
    /// Unity 的 eulerAngles.y = 经度(Yaw), eulerAngles.x = 纬度(Pitch)。
    /// </summary>
    public void SyncAngles()
    {
        Vector3 angles = transform.eulerAngles;
        _x = angles.y;
        _y = NormalizeSignedAngle(angles.x);
    }

    /// <summary>
    /// 每次启用时从当前摄像机朝向重新读取角度，避免 MyCameraManager 在 FreeLook
    /// 模式下改变了摄像机朝向导致首次拖拽出现跳变。
    /// </summary>
    private void OnEnable()
    {
        RestRotationInfo();
    }

    /// <summary>
    /// LateUpdate 保证在 MyCameraManager.Update() 处理完输入之后执行，
    /// 避免同一帧内摄像机位置计算使用了旧的输入数据。
    /// </summary>
    private void LateUpdate()
    {
        if (Target == null)
            return;

        // 读取鼠标移动量，累加到当前角度
        // Input.GetAxis("Mouse X") 返回本帧鼠标在 X 方向的移动量（已乘以灵敏度设置中的值）
        float deltaX = Input.GetAxis("Mouse X") * XSpeed;
        float deltaY = Input.GetAxis("Mouse Y") * YSpeed;

        _x += deltaX;   // 经度累加（水平旋转）
        _y -= deltaY;   // 纬度累加（注意取反：鼠标向上拖 = 视角向下看）
        _y = ClampAngle(_y, YMinLimit, YMaxLimit);             // 钳制垂直角度
        Distance = Mathf.Clamp(Distance, DistanceMin, DistanceMax); // 钳制距离

        // 每次更新都重新计算摄像机位置
        Zoom();
    }

    /// <summary>
    /// 从摄像机的本地欧拉角重新读取当前角度。
    /// MyCameraManager 在 FreeLook 结束后调用此方法，确保轨道模式的初始角度
    /// 与 FreeLook 结束时的视角保持一致（无缝切换）。
    /// </summary>
    public void RestRotationInfo()
    {
        Transform cameraTransform = _camera != null ? _camera.transform : transform;
        _x = cameraTransform.localEulerAngles.y;
        _y = NormalizeSignedAngle(cameraTransform.localEulerAngles.x);
    }

    /// <summary>
    /// 根据当前 _x/_y/Distance 计算摄像机在球壳上的位置。
    ///
    /// 计算方法：
    /// 1. 用 _y（Pitch）和 _x（Yaw）构造四元数旋转
    /// 2. 旋转后的 forward 方向 = 摄像机在球壳上的方向
    /// 3. transform.position = Target.position + rotation * (0, 0, -Distance)
    ///    即：从 Target 出发，沿摄像机朝向的反方向走 Distance 的距离
    ///
    /// 直观理解：摄像机在 Target 的"后方" Distance 米处，看向 Target。
    /// </summary>
    public void Zoom()
    {
        if (Target == null)
            return;

        // 构造旋转：先绕 X 轴转 _y 度（Pitch），再绕 Y 轴转 _x 度（Yaw）
        Quaternion rotation = Quaternion.Euler(_y, _x, 0f);
        transform.rotation = rotation;

        // 摄像机位置 = 目标点 + 旋转后的 (0, 0, -Distance)
        // 即：从目标点沿旋转后的 -Z 方向（摄像机后方）走 Distance 的距离
        Vector3 negativeDistance = new Vector3(0f, 0f, -Distance);
        transform.position = rotation * negativeDistance + Target.position;
    }

    /// <summary>
    /// 角度钳制，处理 -360°~360° 范围的角度限制。
    /// 注意：这里没有用 while 循环处理超出 ±360° 的情况，所以如果 YMinLimit/YMaxLimit
    /// 超出了 ±360 的范围，可能钳制不准确。但通常轨道摄像机的限制范围在 ±90° 内。
    /// </summary>
    public static float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360f)
            angle += 360f;
        if (angle > 360f)
            angle -= 360f;

        return Mathf.Clamp(angle, min, max);
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
