# 寒冰流派 Mod

为《卡片魔王：只剩个头》（DemonLord JustABlock）新增寒冰流派，包含 15 个冰冻主题技能。

## 功能介绍

**流派 ID**: 100260620  
**技能 ID**: 1000260621 ~ 1000260635

### 核心机制

- **冰冻控制**：多种方式冰冻敌人，限制其行动
- **冰冻增伤**：攻击冰冻敌人获得额外伤害
- **连锁反应**：冰冻敌人死亡时扩散冰冻效果

### 技能列表

| 技能 | 效果 |
|------|------|
| 冰拳 | 攻击 20 次冰冻敌人 |
| 霜寒 | 冰冻持续回合 +5 |
| 寒冰之路 | 移动 30 次留下冰面 |
| 冰冷登场 | 开局冰冻 3 个敌人 |
| 冰甲护体 | 受伤后获得 2 回合无敌 |
| 冰魂 | 冰冻敌人时回复 2 HP（多个叠加） |
| 连冻 | 冰冻敌人死亡时冰冻周围敌人 |
| 冰闪 | 闪避时冰冻周围敌人 |
| 寒冰反射 | 受伤时冰冻攻击者（100% 触发） |
| 寒冰领域 | 每回合对冰冻敌人造成 1 点伤害 |
| 老寒腿 | 移动时冰冻附近敌人 |
| 绝对零度 | 敌人被冰冻 3 次后秒杀 |
| 寒霜 | 攻击冰冻敌人时伤害 +2 |
| 冰晶 | 攻击 15 次生成冰面 |
| 冰河世纪 | 每 50 回合全屏冰冻 |

## 安装方法

### 前置依赖

1. [00HarmonyLoader](https://steamcommunity.com/sharedfiles/filedetails/?id=3716839341) - Harmony 补丁加载器
2. [SchoolCardLib](https://github.com/TOKGOK/school-card-lib) - 流派卡片库（前置 Mod）

### 安装步骤

1. 确保已安装上述前置依赖
2. 将 `IceSchoolMod` 目录复制到游戏本地 Mod 目录：
   ```
   C:\Users\<用户名>\AppData\LocalLow\YuWave\DemonLordJustABlock\LocalMods\
   ```
3. 启动游戏，在 Mod 管理界面启用「寒冰流派」

## 依赖说明

| 依赖 | 用途 | 必需 |
|------|------|------|
| 00HarmonyLoader | 提供 Harmony 运行时支持 | ✅ |
| SchoolCardLib | 扩展幸运卡组系统，支持自定义流派 | ✅ |

## 版本历史

### V0.2.8
- 移除调试日志，稳定版发布

### V0.2.7
- 彻底修复寒冰反射：绕过大体型敌人 20 回合冰冻免疫

### V0.2.6
- 修复寒冰反射在 IFrame/无敌状态下不触发的问题

### V0.2.5
- 修复寒冰反射冷却问题，改为每次受伤都触发

### V0.2.4
- 修复冰甲护体目标错误（给玩家而非攻击者无敌）
- 修复寒冰领域伤害数值（1 点而非 3 点，无击退）
- 支持多个冰魂叠加回血

### V0.2.3
- 修复寒冰领域不生效问题

### V0.2.2
- 初始发布

## 技术实现

- 使用 Harmony 进行运行时方法补丁
- 通过 `ExtendedFreezeBuff` 子类绕过大体型冰冻免疫
- 反射调用 SchoolCardLib API 避免编译时依赖

## 开发

### 编译

```bash
cd CodeMods
dotnet build
```

### 项目结构

```
IceSchoolMod/
├── mod.json
├── README.md
├── school_icon.png
├── preview.png
├── AbilityConfigs/
│   └── ModSkillConfigs.csv
└── CodeMods/
    ├── codemod.json
    ├── IceSchoolMod.cs
    └── IceSchoolMod.csproj
```

## 反馈

- **Bug 报告**: [Issues](https://github.com/TOKGOK/ice-school-mod/issues)
- **创意工坊**: [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3748227255)

## 许可证

MIT License
