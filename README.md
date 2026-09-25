# Unity 动作战斗 Demo（URP）

个人开发的 Unity URP 3D 动作游戏演示项目：武士刀连段战斗、时机精确对齐的刀气特效系统、双持大剑 BOSS AI、锁定视角相机。

## 核心内容

### 玩家武士刀战斗
- A1–A4 四段连击：伤害帧窗口、体力消耗、取消点均由动画事件 + 组件驱动（`Assets/Script/Player/`、`Config/`）。
- 体力只限制下一段起手，段内不削伤；慢动作打击感经实测调平。

### 刀气 VFX 系统（本仓库主要工程亮点）
- `SwordSlashController`：几何靠离线烘焙固定弧、揭示量每帧读刀尖实际扫角写入材质标量，保证特效像素严格不超过刀刃真实轨迹；粒子池化 + 按段预设。
- 基于 Hovl Studio 刀剑 VFX 资产扩展的 Wipe 变体 shader 做角向揭示（仓库内不含任何第三方资产源文件，运行需自备）。
- 编辑器调参工具 `SwordSlashTuner`（`Assets/Editor/`）：帧步进检视、弧拟合烘焙、参数持久化到 `SlashTuning.json`。

### BOSS AI
- `BossAIController` + Animator 行为树：旋风连段、防御层、伤害帧系统；数值走 `Config/BossAI.csv` 配表，改表即生效。

### 锁定相机
- Cinemachine 分轴偏移取景模型，冲刺/锁定振荡治理（碰撞体与 Aim 通道分离）。

## 目录速览
- `Assets/Script/` 运行时代码（Player / Base.Hitbox / Boss / Camera）
- `Assets/Editor/` 调参与烘焙工具
- `Assets/Scenes/WYnightDemo.unity` 主演示场景
- `Config/` 战斗配表；`SlashTuning.json` 刀气参数快照

## 运行环境
Unity 6（URP）、Input System、Cinemachine。第三方动画/VFX/模型资产未随仓库分发。

版本：2026-09-26 刀气正式版本。
