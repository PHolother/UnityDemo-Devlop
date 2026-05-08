using UnityEngine;
using UnityEngine.UI;
using Script.Base.BattleAttribute;

/// <summary>
/// BOSS血条管理器
/// 负责控制BOSS血条的显示/隐藏和更新
/// </summary>
public class BossHealthBarManager : MonoBehaviour
{
    [Header("UI引用")]
    [Tooltip("BOSS血条Slider")]
    [SerializeField] private Slider bossHealthSlider;

    [Tooltip("BOSS血条容器（用于整体显示/隐藏）")]
    [SerializeField] private GameObject bossHealthBarContainer;

    [Header("BOSS组件引用")]
    [Tooltip("BOSS的Health组件（在BattleAttribute子物体上）")]
    [SerializeField] private Health bossHealth;

    [Tooltip("BOSS的NoticePlayer组件（用于检测战斗开始）")]
    [SerializeField] private NoticePlayer noticePlayer;

    // 内部状态
    private bool isBattleStarted = false;
    private bool isBossDead = false;

    private void Start()
    {
        // 初始隐藏BOSS血条容器
        if (bossHealthBarContainer != null)
        {
            bossHealthBarContainer.SetActive(false);
        }

        // 验证bossHealth
        if (bossHealth == null)
        {
            Debug.LogError("[BossHealthBarManager] 未设置BOSS的Health组件！");
            return;
        }

        // 验证noticePlayer
        if (noticePlayer == null)
        {
            Debug.LogError("[BossHealthBarManager] 未设置BOSS的NoticePlayer组件！");
            return;
        }

        // 订阅事件
        noticePlayer.OnBattleStart.AddListener(OnBattleStart);
        bossHealth.OnHealthChanged.AddListener(OnBossHealthChanged);

        Debug.Log("[BossHealthBarManager] 初始化完成，等待战斗开始");
    }

    /// <summary>
    /// 战斗开始回调
    /// </summary>
    private void OnBattleStart()
    {
        // 避免重复触发
        if (isBattleStarted)
        {
            return;
        }

        ShowBossHealthBar();
        isBattleStarted = true;
        UpdateHealthBarDisplay();

        Debug.Log("[BossHealthBarManager] BOSS血条已显示");
    }

    /// <summary>
    /// BOSS血量变化回调
    /// </summary>
    /// <param name="change">血量变化量</param>
    private void OnBossHealthChanged(int change)
    {
        // 双重保险：首次受伤也触发显示
        if (!isBattleStarted && change < 0)
        {
            OnBattleStart();
        }

        UpdateHealthBarDisplay();

        // 检查BOSS是否死亡
        if (bossHealth.IsDead() && !isBossDead)
        {
            OnBossDeath();
        }
    }

    /// <summary>
    /// 更新血条显示
    /// </summary>
    private void UpdateHealthBarDisplay()
    {
        if (bossHealthSlider == null)
        {
            return;
        }

        // 计算血量百分比
        float healthPercent = (float)bossHealth.GetCurrentHealth() / bossHealth.maxHealth;
        bossHealthSlider.value = healthPercent;

        Debug.Log($"[BossHealthBarManager] BOSS血量变化: {bossHealth.GetCurrentHealth()}/{bossHealth.maxHealth}");
    }

    /// <summary>
    /// BOSS死亡回调
    /// </summary>
    private void OnBossDeath()
    {
        isBossDead = true;

        // 延迟2秒隐藏血条
        Invoke(nameof(HideBossHealthBar), 2f);

        Debug.Log("[BossHealthBarManager] BOSS已死亡，准备隐藏血条");
    }

    /// <summary>
    /// 显示BOSS血条
    /// </summary>
    private void ShowBossHealthBar()
    {
        if (bossHealthBarContainer != null)
        {
            bossHealthBarContainer.SetActive(true);
        }
    }

    /// <summary>
    /// 隐藏BOSS血条
    /// </summary>
    private void HideBossHealthBar()
    {
        if (bossHealthBarContainer != null)
        {
            bossHealthBarContainer.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        // 取消事件订阅
        if (noticePlayer != null)
        {
            noticePlayer.OnBattleStart.RemoveListener(OnBattleStart);
        }

        if (bossHealth != null)
        {
            bossHealth.OnHealthChanged.RemoveListener(OnBossHealthChanged);
        }
    }
}
