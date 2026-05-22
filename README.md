# SaveManager — 文件级多存档、死亡回档与 UI 接管

- 把当前 `save.sv` 复制成独立槽位，槽位按 `runId/日期` 两层目录组织
- 为每个槽位写一份同名 `.json` sidecar，保存昵称、是否自动备份、是否 pinned、最后游玩时间、玩家位置、QoL 种子等元数据
- 在运行中的冒险里定时备份，并且按“每个 `runId` 单独保留 N 份”的规则滚动删除旧自动槽
- 在角色死亡时启动回档倒计时，必要时拦住游戏自己的“重生世界 / 回主菜单”流程，改为加载最近一份可回档槽位。
- 在检测到 `QoL` 或 `KrokoshaCasualtiesMP` 时做软联动；这些 mod 缺席或字段签名变化时，不影响单机的基本存档功能，只会让“位置恢复 / 种子恢复 / 多人广播 / 多人死亡判定”降级，可能会出现一些问题

可以看我另一个项目：[CasualtiesUnknown-SkinEditor 皮肤编辑器](https://github.com/huanxin996/CasualtiesUnknown-SkinEditor)，支持实时预览和动画预览

提到的motionRecorder  references/ 在CasualtiesUnknown-SkinEditor有说明
