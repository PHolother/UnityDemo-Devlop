using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 鼠标视角死区处理器：|delta| 小于 min（像素）的鼠标位移被归零。
/// 消除鼠标静止噪声（手部微抖/硬件噪声）驱动的视角微转——
/// 玩家前进时视角被噪声带偏导致走不直。
/// 只做死区、不缩放幅度，完全不改变灵敏度。
/// </summary>
public class MouseDeltaDeadzoneProcessor : InputProcessor<Vector2>
{
    [Tooltip("|delta| 小于此值（像素）的鼠标位移被归零")]
    public float min = 1f;

    public override Vector2 Process(Vector2 value, InputControl control)
    {
        if (value.magnitude < min) return Vector2.zero;
        return value;
    }
}
