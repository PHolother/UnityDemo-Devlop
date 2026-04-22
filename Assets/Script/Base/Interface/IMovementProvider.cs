using UnityEngine;

public interface IMovementProvider
{
    // 移动控制
    bool CanMove { get; set; }
    bool CanRotate { get; set; }
    
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