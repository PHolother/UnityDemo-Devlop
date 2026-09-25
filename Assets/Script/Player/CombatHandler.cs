using UnityEngine;
using Script.Base.Interface.Battle;

/// <summary>
/// 战斗事件处理器
/// 接收HitboxController的OnHit事件，处理伤害应用、音效、卡肉、镜头摇晃
/// 挂载在 Player 上
/// </summary>
public class CombatHandler : MonoBehaviour
{
    [Header("音效引用")]
    [Tooltip("命中音效")]
    public AudioClip hitSound;

    [Header("卡肉配置")]
    [Tooltip("卡肉持续时间（秒）")]
    public float hitStopDuration = 0.08f;

    [Header("镜头摇晃配置")]
    [Tooltip("命中时镜头摇晃强度倍率（1=默认强度）")]
    public float cameraShakeIntensity = 1f;

    [Header("引用")]
    [Tooltip("镜头摇晃控制器（留空则自动查找 Player Camara）")]
    [SerializeField] private CameraShakeController cameraShakeController;

    private void Start()
    {
        if (cameraShakeController == null)
        {
            var cameraObj = GameObject.Find("Player Camara");
            if (cameraObj != null)
                cameraShakeController = cameraObj.GetComponent<CameraShakeController>();
        }
    }

    /// <summary>
    /// HitboxController的OnHit事件绑定此方法
    /// 参数：attacker, battleAttribute, targetRoot, damage
    /// </summary>
    public void HandleHit(GameObject attacker, GameObject battleAttribute, GameObject target, int damage)
    {
        DamageProbe("HandleHit 进入 attacker=" + attacker.name + " target=" + target.name + " damage=" + damage);
        // 0. 受击方向暂存：判断正面/背面受击（玩家受击动画选择用）。
        //    attacker 为攻击者根物体，target 为受击根物体（玩家）。
        if (attacker != null && target != null)
        {
            var hurtCtrl = target.GetComponentInChildren<PlayerHurtController>();
            if (hurtCtrl != null)
            {
                hurtCtrl.SetHitDirection(attacker.transform.position);
            }
        }

        // 1. 应用伤害到目标
        ApplyDamage(target, damage);

        // 2. 播放命中音效
        PlayHitSound();

        // 3. 触发卡肉
        if (HitstopManager.Instance != null)
            HitstopManager.Instance.TriggerHitstop(hitStopDuration);

        // 4. 触发镜头摇晃
        if (cameraShakeController != null)
            cameraShakeController.TriggerShake(cameraShakeIntensity);
    }

    /// <summary>
    /// 对目标应用伤害
    /// </summary>
    private void ApplyDamage(GameObject target, int damage)
    {
        IDamageable damageable = target.GetComponentInChildren<IDamageable>();
        DamageProbe("ApplyDamage target=" + target.name + " damage=" + damage + " 找到IDamageable=" + (damageable != null ? ((MonoBehaviour)damageable).gameObject.name : "NULL"));
        if (damageable != null)
        {
            bool ok = damageable.TakeDamage(damage);
            DamageProbe("TakeDamage 返回=" + ok);
            return;
        }

        target.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
        DamageProbe("SendMessage TakeDamage fallback");
    }

    /// <summary>
    /// 伤害链路探针（临时）：写项目根 ProbeLogs/damage_chain_log.txt（避免 Temp 被清）。
    /// </summary>
    private void DamageProbe(string msg)
    {
        try
        {
            string root = System.IO.Directory.GetParent(Application.dataPath).FullName;
            string dir = System.IO.Path.Combine(root, "ProbeLogs");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "damage_chain_log.txt");
            System.IO.File.AppendAllText(path, "t=" + Time.time.ToString("0.000") + " " + msg + System.Environment.NewLine);
        }
        catch (System.Exception) { }
    }

    /// <summary>
    /// 播放命中音效
    /// </summary>
    private void PlayHitSound()
    {
        if (hitSound != null)
        {
            AudioSource.PlayClipAtPoint(hitSound, transform.position);
        }
    }
}
