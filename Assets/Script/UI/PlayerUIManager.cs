using UnityEngine;
using UnityEngine.UI;
using Script.Base.BattleAttribute;

/// <summary>
/// 玩家UI管理器
/// 负责更新玩家的血量、法力UI显示
/// </summary>
public class PlayerUIManager : MonoBehaviour
{
    [Header("UI引用")]
    [Tooltip("血量条Slider")]
    [SerializeField] private Slider healthSlider;

    [Tooltip("法力条Slider")]
    [SerializeField] private Slider manaSlider;

    [Tooltip("体力条Slider")]
    [SerializeField] private Slider staminaSlider;

    [Header("玩家属性组件引用")]
    [Tooltip("玩家的BattleAttributes组件（统一管理器）")]
    [SerializeField] public BattleAttributes battleAttributes;

    [Header("虚条（延迟衰减）配置")]
    [Tooltip("虚条开始消退前的延迟（秒）：此时间内不再被扣才消退（由 PlayerConfig.xlsx PlayerAttribute 的 ghostFadeDelay 配置）")]
    public float ghostFadeDelay = 1f;
    [Tooltip("虚条每秒消退量（由 PlayerConfig.xlsx PlayerAttribute 的 ghostFadeRate 配置）")]
    public float ghostFadeRate = 20f;

    // 内部字段
    private Health playerHealth;
    private Mana playerMana;
    private Stamina playerStamina;

    // 虚条（延迟衰减）组件：血/蓝/体力各一
    private DelayedBar healthGhost;
    private DelayedBar manaGhost;
    private DelayedBar staminaGhost;

    private void Awake()
    {
        // 只验证 battleAttributes 是否为 null
        if (battleAttributes == null)
        {
            Debug.LogError("[PlayerUIManager] 未设置玩家的BattleAttributes组件！");
            return;
        }
    }

    private void Start()
    {
        // 在 Start 中获取组件引用（确保 BattleAttributes.Awake 已执行）
        playerHealth = battleAttributes.Health;
        playerMana = battleAttributes.Mana;
        playerStamina = battleAttributes.Stamina;

        if (playerHealth == null)
        {
            Debug.LogError("[PlayerUIManager] BattleAttributes中未找到Health组件！");
            return;
        }

        // 订阅事件
        playerHealth.OnHealthChanged.AddListener(UpdateHealthBar);

        if (playerMana != null)
        {
            playerMana.OnManaChanged.AddListener(UpdateManaBar);
        }

        if (playerStamina != null)
        {
            // 体力条未在场景配置时动态创建（法力条下方，黄色）。运行时创建绕开场景序列化
            // 的 RectTransform 值丢失问题，每次 Play 都按法力条位置重建。
            if (staminaSlider == null && manaSlider != null)
            {
                staminaSlider = CreateStaminaSlider();
            }
            // 体力恢复是逐帧平滑增长的，每帧直接同步 UI（消耗瞬间由事件触发立即刷新）
            playerStamina.OnStaminaChanged.AddListener(UpdateStaminaBar);
        }

        // 初始化虚条（延迟衰减）：血浅红 / 蓝浅蓝 / 体力浅黄（半透明）
        // 配置（消退延迟/速度）来自 PlayerConfig.xlsx PlayerAttribute 的 ghostFadeDelay/ghostFadeRate
        if (healthSlider != null)
        {
            healthGhost = healthSlider.gameObject.AddComponent<DelayedBar>();
            healthGhost.ApplyGhostColor(new Color(1f, 0.6f, 0.6f, 0.5f));
            healthGhost.Configure(ghostFadeDelay, ghostFadeRate);
        }
        if (manaSlider != null)
        {
            manaGhost = manaSlider.gameObject.AddComponent<DelayedBar>();
            manaGhost.ApplyGhostColor(new Color(0.6f, 0.75f, 1f, 0.5f));
            manaGhost.Configure(ghostFadeDelay, ghostFadeRate);
        }
        if (staminaSlider != null)
        {
            staminaGhost = staminaSlider.gameObject.AddComponent<DelayedBar>();
            staminaGhost.ApplyGhostColor(new Color(1f, 0.95f, 0.6f, 0.5f));
            staminaGhost.Configure(ghostFadeDelay, ghostFadeRate);
        }

        // 初始化UI
        InitializeUI();

        Debug.Log("[PlayerUIManager] 初始化完成");
    }

    /// <summary>
    /// 初始化UI显示
    /// </summary>
    private void InitializeUI()
    {
        if (healthSlider != null && playerHealth != null)
        {
            healthSlider.maxValue = playerHealth.maxHealth;
            healthSlider.value = playerHealth.GetCurrentHealth();
            Debug.Log($"[PlayerUIManager] 血条初始化: {playerHealth.GetCurrentHealth()}/{playerHealth.maxHealth}");
        }

        if (manaSlider != null && playerMana != null)
        {
            manaSlider.maxValue = playerMana.maxMana;
            manaSlider.value = playerMana.GetCurrentMana();
            Debug.Log($"[PlayerUIManager] 法力条初始化: {playerMana.GetCurrentMana()}/{playerMana.maxMana}");
        }

        if (staminaSlider != null && playerStamina != null)
        {
            staminaSlider.maxValue = playerStamina.maxStamina;
            staminaSlider.value = playerStamina.GetCurrentStamina();
            Debug.Log($"[PlayerUIManager] 体力条初始化: {playerStamina.GetCurrentStamina():0.#}/{playerStamina.maxStamina:0.#}");
        }
    }

    /// <summary>
    /// 更新血条显示
    /// </summary>
    /// <param name="change">血量变化量（负=扣血）</param>
    private void UpdateHealthBar(int change)
    {
        if (healthSlider == null || playerHealth == null)
        {
            return;
        }

        // 原条瞬间 + 虚条补/消退
        if (healthGhost != null)
        {
            healthGhost.SetCurrentValue(playerHealth.GetCurrentHealth(), change);
        }
        else
        {
            healthSlider.value = playerHealth.GetCurrentHealth();
        }
        Debug.Log($"[PlayerUIManager] 血量变化: {change}, 当前: {playerHealth.GetCurrentHealth()}/{playerHealth.maxHealth}");
    }

    /// <summary>
    /// 更新法力条显示
    /// </summary>
    /// <param name="change">法力变化量（负=消耗）</param>
    private void UpdateManaBar(int change)
    {
        if (manaSlider == null || playerMana == null)
        {
            return;
        }

        if (manaGhost != null)
        {
            manaGhost.SetCurrentValue(playerMana.GetCurrentMana(), change);
        }
        else
        {
            manaSlider.value = playerMana.GetCurrentMana();
        }
        Debug.Log($"[PlayerUIManager] 法力变化: {change}, 当前: {playerMana.GetCurrentMana()}/{playerMana.maxMana}");
    }

    /// <summary>
    /// 动态创建体力条（法力条下方，黄色填充）。
    /// 场景未配置 staminaSlider 时在 Start 调用——运行时创建绕开场景序列化
    /// 的 RectTransform 值丢失问题，每次 Play 按法力条位置重建。
    /// 定位用 offsetMin/offsetMax（绝对偏移）：anchoredPosition/sizeDelta 在
    /// Slider 初始化后设置会被 offset 覆盖（实测 pos/size 被重置为 0）。
    /// </summary>
    private Slider CreateStaminaSlider()
    {
        if (manaSlider == null) return null;
        var manaRt = manaSlider.GetComponent<RectTransform>();
        if (manaRt == null) return null;

        // 父 = 法力条的父（PlayerUIPanel）
        var parent = manaRt.parent;
        if (parent == null) return null;

        var stamGo = new GameObject("StaminaBarSlider", typeof(RectTransform));
        stamGo.transform.SetParent(parent, false);
        var stamRt = stamGo.GetComponent<RectTransform>();
        // 锚点与法力条一致（左上角顶部锚点）
        stamRt.anchorMin = manaRt.anchorMin;
        stamRt.anchorMax = manaRt.anchorMax;
        stamRt.pivot = manaRt.pivot;

        // 先建 Fill（黄色），再挂 Slider 并关联 fillRect，最后设 offset（顺序关键：
        // Slider OnEnable 初始化时不重置已设置的 RectTransform）
        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(stamGo.transform, false);
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        var fillImg = fillGo.AddComponent<UnityEngine.UI.Image>();
        fillImg.color = new Color(1f, 0.85f, 0.2f, 1f);

        // Slider
        var slider = stamGo.AddComponent<Slider>();
        slider.fillRect = fillRt;
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.value = 100f;
        slider.interactable = false;

        // 最后定位：体力条 = 法力条下方（法力条 offsetMin.y 往下偏移"法力条高+间隔"）。
        // 法力条高度 = offsetMax.y - offsetMin.y（不用 sizeDelta——Start 早期可能未初始化）。
        // 法力条 offsetMin=(5,-60) offsetMax=(500,-35) 高25 → 体力条 offsetMin.y=-60-30=-90
        //（25 高 + 5 间隔），体力条高 20 → offsetMax.y=-70。
        float barHeight = 20f;
        float gap = 5f;
        float manaHeight = manaRt.offsetMax.y - manaRt.offsetMin.y;
        if (manaHeight <= 0f) manaHeight = 25f;
        float stamMinY = manaRt.offsetMin.y - manaHeight - gap;
        float stamMaxY = stamMinY + barHeight;
        stamRt.offsetMin = new Vector2(manaRt.offsetMin.x, stamMinY);
        stamRt.offsetMax = new Vector2(manaRt.offsetMax.x, stamMaxY);

        return slider;
    }

    /// <summary>
    /// 更新体力条显示。消耗是瞬间的（事件触发立即刷新）；恢复是逐帧平滑增长
    /// （Stamina.Update 每帧触发事件，UI 平滑跟进）。
    /// </summary>
    /// <param name="change">体力变化量（负=消耗，正=恢复）</param>
    private void UpdateStaminaBar(float change)
    {
        if (staminaSlider == null || playerStamina == null)
        {
            return;
        }

        if (staminaGhost != null)
        {
            staminaGhost.SetCurrentValue(playerStamina.GetCurrentStamina(), change);
        }
        else
        {
            staminaSlider.value = playerStamina.GetCurrentStamina();
        }
    }

    private void OnDestroy()
    {
        // 取消事件订阅
        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged.RemoveListener(UpdateHealthBar);
        }

        if (playerMana != null)
        {
            playerMana.OnManaChanged.RemoveListener(UpdateManaBar);
        }

        if (playerStamina != null)
        {
            playerStamina.OnStaminaChanged.RemoveListener(UpdateStaminaBar);
        }
    }
}
