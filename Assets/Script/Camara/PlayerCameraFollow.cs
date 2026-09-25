using Cinemachine;
using UnityEngine;

/// <summary>
/// 相机跟随初始化 — 配置 FreeLook 使用 Proxy 作为 Follow 和 LookAt 目标
/// Proxy 滞后时相机注视点落后于玩家，使玩家在屏幕上向移动方向偏移
/// </summary>
public class PlayerCameraFollow : MonoBehaviour
{
    [SerializeField] private CinemachineFreeLook freeLook;
    [SerializeField] private CameraFollowProxy followProxy;

    [Tooltip("LookAt 目标高度偏移（匹配玩家 LookAt 子物体的高度）")]
    [SerializeField] private float lookAtHeight = 1.8f;

    private void Start()
    {
        if (freeLook == null)
            freeLook = GetComponent<CinemachineFreeLook>();

        if (followProxy != null && freeLook != null)
        {
            // Follow = Proxy：轨道中心随 Proxy 滞后
            freeLook.Follow = followProxy.transform;

            // LookAt = Proxy：相机注视 Proxy 位置，玩家因领先 Proxy 而在屏幕上偏移
            freeLook.LookAt = followProxy.transform;
        }

        ResetComposerSettings();
        ResetOrbitalDamping();
    }

    private void ResetComposerSettings()
    {
        if (freeLook == null) return;

        for (int i = 0; i < 3; i++)
        {
            var rig = freeLook.GetRig(i);
            if (rig == null) continue;

            var composer = rig.GetCinemachineComponent<CinemachineComposer>();
            if (composer == null) continue;

            composer.m_BiasX = 0f;
            composer.m_BiasY = 0f;
            // 高度偏移：让相机注视 Proxy 上方 lookAtHeight 处，匹配原 LookAt 子物体高度
            composer.m_TrackedObjectOffset = new Vector3(0f, lookAtHeight, 0f);
        }
    }

    private void ResetOrbitalDamping()
    {
        if (freeLook == null) return;

        for (int i = 0; i < 3; i++)
        {
            var rig = freeLook.GetRig(i);
            if (rig == null) continue;

            var orbitalTransposer = rig.GetCinemachineComponent<CinemachineOrbitalTransposer>();
            if (orbitalTransposer == null) continue;

            orbitalTransposer.m_XDamping = 0f;
            orbitalTransposer.m_YDamping = 0f;
            orbitalTransposer.m_ZDamping = 0f;
        }
    }
}
