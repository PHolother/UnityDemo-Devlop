using UnityEngine;
using UnityEngine.UI;
using Script.Base.BattleAttribute;

/// <summary>
/// 玩家UI管理器
/// 负责更新玩家的血量、法力、体力UI显示
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
    [SerializeField] private BattleAttributes battleAttributes;

    // 内部字段
    private Health playerHealth;
    private Mana playerMana;
    private Stamina playerStamina;

    private void Awake()
    {
        // 验证battleAttributes是否为null
        if (battleAttributes == null)
        {
            Debug.LogError("[PlayerUIManager] 未设置玩家的BattleAttributes组件！");
            return;
        }

        // 通过battleAttributes获取组件
        playerHealth = battleAttributes.Health;
        playerMana = battleAttributes.Mana;
        playerStamina = battleAttributes.Stamina;

        // 验证playerHealth是否为null
        if (playerHealth == null)
        {
            Debug.LogError("[PlayerUIManager] BattleAttributes中未找到Health组件！");
        }
    }

    private void Start()
    {
        if (playerHealth == null)
        {
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
            playerStamina.OnStaminaChanged.AddListener(UpdateStaminaBar);
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
            Debug.Log($"[PlayerUIManager] 体力条初始化: {playerStamina.GetCurrentStamina()}/{playerStamina.maxStamina}");
        }
    }

    /// <summary>
    /// 更新血条显示
    /// </summary>
    /// <param name="change">血量变化量</param>
    private void UpdateHealthBar(int change)
    {
        if (healthSlider == null || playerHealth == null)
        {
            return;
        }

        healthSlider.value = playerHealth.GetCurrentHealth();
        Debug.Log($"[PlayerUIManager] 血量变化: {change}, 当前: {playerHealth.GetCurrentHealth()}/{playerHealth.maxHealth}");
    }

    /// <summary>
    /// 更新法力条显示
    /// </summary>
    /// <param name="change">法力变化量</param>
    private void UpdateManaBar(int change)
    {
        if (manaSlider == null || playerMana == null)
        {
            return;
        }

        manaSlider.value = playerMana.GetCurrentMana();
        Debug.Log($"[PlayerUIManager] 法力变化: {change}, 当前: {playerMana.GetCurrentMana()}/{playerMana.maxMana}");
    }

    /// <summary>
    /// 更新体力条显示
    /// </summary>
    /// <param name="change">体力变化量</param>
    private void UpdateStaminaBar(int change)
    {
        if (staminaSlider == null || playerStamina == null)
        {
            return;
        }

        staminaSlider.value = playerStamina.GetCurrentStamina();
        Debug.Log($"[PlayerUIManager] 体力变化: {change}, 当前: {playerStamina.GetCurrentStamina()}/{playerStamina.maxStamina}");
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
