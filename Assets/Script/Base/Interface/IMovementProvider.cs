using UnityEngine;

public interface IMovementProvider
{
    // 移动控制（只读派生：任一硬控锁激活则不可移动/旋转；全部解锁才可）
    bool CanMove { get; }
    bool CanRotate { get; }

    // 独立硬控锁：攻击连段锁 与 受击硬直锁 分开管理。
    // 锁为或：任一锁激活 → 不能移动；恢复为与：必须所有锁都解除才可移动。
    void SetAttackLock(bool locked);
    void SetHurtLock(bool locked);
    
    // 输入检测
    bool IfMove();
    Vector3 GetMoveDirection();
    
    // 速度
    float GetCurrentSpeed();
    float GetSprintSpeed();
    void SetSprintState(bool sprint);
    
    // 旋转
    float GetRotateSpeed();
    
    // 预输入攻击转向
    void EnableBuffedRotate();// 攻击开始时调用，允许本次攻击转向
    void DisableBuffedRotate();// 攻击结束时调用，禁止转向
}