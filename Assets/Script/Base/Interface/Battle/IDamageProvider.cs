namespace Script.Base.Interface.Battle
{
    /// <summary>
    /// 伤害提供者接口，由战斗属性脚本实现
    /// </summary>
    public interface IDamageProvider
    {
        /// <summary>
        /// 获取当前伤害值
        /// </summary>
        int GetDamage();
    }
}
