

## Codely Structured Memories

### User

### Feedback

### Project
- [2026-07-22 00:44:47] BOSS animation clips have NO Animation Events. Detect attack end via BossAIController.IsInAttackAnimation() checking AnimatorStateInfo.IsName() against AttackStateNames HashSet — do NOT rely on Animation Event callbacks. Write Defaults must be OFF on all Test.controller states. LoopTime ON only for: Common_Idle, Common_Walk_Loop, Strafe_Walk variants, Strafe_Run_F, Defense_Loop, Whirlwind_Loop, FocusEnergy_Loop. All other clips: LoopTime OFF.
### Reference

