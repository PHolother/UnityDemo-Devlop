using UnityEngine;

/// <summary>
/// 【临时】Boss 行为观察相机开关。仅供调试，观察完可删除整个 TempObserverCam。
/// 按 F9 开启/关闭相机（仅开关 Camera 组件，不影响其它逻辑）。
/// </summary>
public class TempCameraToggle : MonoBehaviour
{
    [Tooltip("按 F9 切换此相机的启用状态")]
    public KeyCode toggleKey = KeyCode.F9;

    private Camera targetCamera;

    private void Awake()
    {
        targetCamera = GetComponent<Camera>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            targetCamera.enabled = !targetCamera.enabled;
            Debug.Log("[TempObserverCam] enabled=" + targetCamera.enabled);
        }
    }
}
