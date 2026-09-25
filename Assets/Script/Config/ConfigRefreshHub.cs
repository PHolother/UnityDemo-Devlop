using System.Collections.Generic;
using UnityEngine;

namespace Script.Config
{
    /// <summary>
    /// 配表刷新中心：所有配表应用器（BossConfigApplier、未来的 PlayerConfigApplier 等）
    /// 在 OnEnable 注册、OnDisable 注销。编辑器菜单 Window/Refresh Config CSV 调用
    /// RefreshAll() 一次性刷新所有已注册的配表。
    /// 挂载：无需场景物体，纯静态注册表。
    /// </summary>
    public static class ConfigRefreshHub
    {
        private static readonly List<MonoBehaviour> appliers = new List<MonoBehaviour>();

        public static void Register(MonoBehaviour applier)
        {
            if (applier == null) return;
            if (!appliers.Contains(applier)) appliers.Add(applier);
        }

        public static void Unregister(MonoBehaviour applier)
        {
            if (applier == null) return;
            appliers.Remove(applier);
        }

        /// <summary>
        /// 刷新所有已注册的配表应用器（通过接口/反射调用其 ApplyConfig）。
        /// </summary>
        public static void RefreshAll()
        {
            // 清理已被销毁的注册项
            appliers.RemoveAll(a => a == null);

            int count = 0;
            foreach (var applier in appliers)
            {
                var method = applier.GetType().GetMethod("ApplyConfig",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (method != null)
                {
                    method.Invoke(applier, null);
                    count++;
                }
            }

            if (count > 0)
            {
                Debug.Log($"[ConfigRefreshHub] 已刷新 {count} 个配表应用器");
            }
            else
            {
                Debug.LogWarning("[ConfigRefreshHub] 没有找到任何已注册的配表应用器（检查场景中是否挂载了 *ConfigApplier 组件）");
            }
        }
    }
}
