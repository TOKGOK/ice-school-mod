using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace IceSchoolMod
{
    /// <summary>
    /// 寒冰流派 Mod V0.2.8
    ///
    /// 流派 ID: 100260620 (完全隔离)
    /// 技能 ID: 1000260621 ~ 1000260635 (10 位数)
    ///
    /// V0.2.8 变更：
    /// - 移除调试日志，准备发布
    ///
    /// 依赖：SchoolCardLib (流派卡片库) - 运行时动态加载
    /// </summary>
    public class Main : SimpleModBehaviour
    {
        // 寒冰流派 ID
        private const int IceSchoolId = 100260620;

        // 技能 ID 范围
        private const int SkillBaseId = 1000260621;
        private const int SkillCount = 15;

        // 融合配方 ID 起始
        private const int FusionBaseId = 1000260651;

        // 运行时注册的技能 ID 列表
        private readonly List<int> runtimeSkillConfigIds = new List<int>();

        // Harmony 实例
        private static Harmony _harmony;

        // 是否已初始化
        private bool _schoolDone;

        // 日志前缀
        private const string LogPrefix = "[寒冰流派]";

        // ========== 冰冻机制自定义参数 ==========
        // 这些字段不在 BattleObject 中，需要手动读取被动技能值

        // 霜寒: 冰冻持续回合加成 (技能 1000260622, paramName=freezeTime, 默认+5)
        private static int _freezeTimeBonus;

        // 绝对零度: 冰冻N次后秒杀阈值 (技能 1000260632, paramName=freezeKillCount, 默认3)
        private static int _freezeKillThreshold;

        // 连冻: 冰冻敌人死亡时冰冻周围的范围 (技能 1000260627, paramName=spreadRange, 默认1)
        private static int _spreadRange;

        // 寒霜: 攻击冰冻敌人时额外伤害 (技能 1000260633, paramName=frozenAtkBonus, 默认2)
        private static int _frozenAtkBonus;

        // 冰魂: 冰冻敌人时回复HP (技能 1000260626, paramName=iceSoulHeal, 默认2)
        private static int _iceSoulHeal;

        // 寒冰领域: 每回合对冰冻敌人造成伤害 (技能 1000260630, paramName=iceDomainDamage, 默认1)
        private static int _iceDomainDamage;

        // 冰甲护体: 无敌持续回合 (技能 1000260625, param2=2)
        private static int _iceArmorDuration;

        // 寒冰领域上次触发的 actionTurn (防止同回合重复触发)
        private static int _lastIceDomainActionTurn = -1;

        // 追踪每个单位的冰冻次数 (RuntimeHelpers.GetHashCode -> 冰冻次数)
        private static readonly Dictionary<int, int> _freezeKillCounter = new Dictionary<int, int>();

        /// <summary>
        /// Mod 加载时调用
        /// </summary>
        public override void OnModLoaded()
        {
            Log("V0.2.8 已加载。流派 ID: " + IceSchoolId);

            // 运行时注册寒冰流派到 SchoolCardLib
            RegisterToSchoolCardLib();

            // 注册技能配置
            RegisterIceSkills();

            // 订阅事件
            BattleObject.OnGameStart += OnGameStart;
            BattleObject.OnLevelStart += OnLevelStart;
            BattleObject.OnAfterHomeDataLoad += OnAfterHomeDataLoad;

            // 延迟初始化 Harmony，等待 00HarmonyLoader 先加载
            StartCoroutine(InitHarmonyDelayed());
        }

        /// <summary>
        /// 延迟初始化 Harmony
        /// </summary>
        private System.Collections.IEnumerator InitHarmonyDelayed()
        {
            // 等待 3 秒，确保 SchoolCardLib 和 00HarmonyLoader 先加载
            yield return new WaitForSeconds(3f);

            Log("开始延迟初始化 Harmony...");
            InitHarmony();
        }

        /// <summary>
        /// 运行时注册到 SchoolCardLib
        /// </summary>
        private void RegisterToSchoolCardLib()
        {
            try
            {
                // 查找 SchoolCardLib 程序集
                Assembly schoolCardLibAssembly = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name == "SchoolCardLib")
                    {
                        schoolCardLibAssembly = assembly;
                        break;
                    }
                }

                if (schoolCardLibAssembly == null)
                {
                    Log("警告：未找到 SchoolCardLib 程序集，尝试加载...");
                    // 尝试从 LocalMods 加载
                    string schoolCardLibPath = System.IO.Path.Combine(
                        Application.persistentDataPath,
                        "LocalMods",
                        "SchoolCardLib",
                        "CodeMods",
                        "SchoolCardLib.dll"
                    );
                    if (System.IO.File.Exists(schoolCardLibPath))
                    {
                        schoolCardLibAssembly = Assembly.LoadFrom(schoolCardLibPath);
                        Log($"已加载 SchoolCardLib: {schoolCardLibPath}");
                    }
                    else
                    {
                        Log($"错误：SchoolCardLib.dll 不存在于 {schoolCardLibPath}");
                        return;
                    }
                }

                // 查找 SchoolCardLib.Main 类型
                Type schoolCardLibMainType = schoolCardLibAssembly.GetType("SchoolCardLib.Main");
                if (schoolCardLibMainType == null)
                {
                    Log("错误：未找到 SchoolCardLib.Main 类型");
                    return;
                }

                // 调用 RegisterSchool 方法
                MethodInfo registerSchoolMethod = schoolCardLibMainType.GetMethod("RegisterSchool",
                    BindingFlags.Public | BindingFlags.Static);

                if (registerSchoolMethod != null)
                {
                    // 查找图标文件路径
                    string iconPath = null;
                    string[] possiblePaths = new string[]
                    {
                        // 本地 Mod 目录
                        System.IO.Path.Combine(Application.persistentDataPath, "LocalMods", "IceSchoolMod", "school_icon.png"),
                        // 创意工坊目录 (ID: 3748227255)
                        "D:/Program Files (x86)/Steam/steamapps/workshop/content/3720420/3748227255/school_icon.png"
                    };

                    foreach (string path in possiblePaths)
                    {
                        if (System.IO.File.Exists(path))
                        {
                            iconPath = path;
                            Log($"找到图标文件：{path}");
                            break;
                        }
                    }

                    // 调用 RegisterSchool，传递图标路径
                    registerSchoolMethod.Invoke(null, new object[]
                    {
                        IceSchoolId,
                        "寒冰",
                        "#00BFFF",
                        null,      // icon (不直接提供)
                        iconPath,  // iconPath (提供文件路径)
                        null       // unlockCondition
                    });
                    Log("已通过反射注册寒冰流派到 SchoolCardLib");
                }
                else
                {
                    Log("错误：未找到 SchoolCardLib.Main.RegisterSchool 方法");
                }
            }
            catch (Exception ex)
            {
                Log($"注册到 SchoolCardLib 失败：{ex.Message}");
                Log($"堆栈：{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Mod 卸载时调用
        /// </summary>
        public override void OnModUnloaded()
        {
            // 取消事件订阅
            BattleObject.OnGameStart -= OnGameStart;
            BattleObject.OnLevelStart -= OnLevelStart;
            BattleObject.OnAfterHomeDataLoad -= OnAfterHomeDataLoad;

            // 移除 Harmony 补丁
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }

            // 清理静态状态
            _freezeKillCounter.Clear();
            _freezeTimeBonus = 0;
            _freezeKillThreshold = 0;
            _spreadRange = 0;
            _frozenAtkBonus = 0;
            _iceSoulHeal = 0;
            _iceDomainDamage = 0;
            _iceArmorDuration = 0;
            _lastIceDomainActionTurn = -1;

            // 移除运行时配置
            RemoveRuntimeConfigs();

            // 运行时取消注册寒冰流派
            UnregisterFromSchoolCardLib();

            Log("已卸载。");
        }

        /// <summary>
        /// 运行时从 SchoolCardLib 取消注册
        /// </summary>
        private void UnregisterFromSchoolCardLib()
        {
            try
            {
                Assembly schoolCardLibAssembly = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name == "SchoolCardLib")
                    {
                        schoolCardLibAssembly = assembly;
                        break;
                    }
                }

                if (schoolCardLibAssembly == null) return;

                Type schoolCardLibMainType = schoolCardLibAssembly.GetType("SchoolCardLib.Main");
                if (schoolCardLibMainType == null) return;

                MethodInfo unregisterSchoolMethod = schoolCardLibMainType.GetMethod("UnregisterSchool",
                    BindingFlags.Public | BindingFlags.Static);

                if (unregisterSchoolMethod != null)
                {
                    unregisterSchoolMethod.Invoke(null, new object[] { IceSchoolId });
                    Log("已通过反射从 SchoolCardLib 取消注册寒冰流派");
                }
            }
            catch (Exception ex)
            {
                Log($"从 SchoolCardLib 取消注册失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update()
        {
            // 首帧添加流派到解锁列表
            if (!_schoolDone)
            {
                _schoolDone = true;
                AddSchoolToUnlockList();
            }
        }

        /// <summary>
        /// 初始化 Harmony 补丁
        /// </summary>
        private void InitHarmony()
        {
            try
            {
                _harmony = new Harmony("com.tokgok.iceschoolmod");

                // ========== 补丁 1: InitSchoolData (确保流派在幸运卡组中可用) ==========
                var initSchoolDataMethod = typeof(BattleObject).GetMethod("InitSchoolData",
                    BindingFlags.Public | BindingFlags.Instance);

                if (initSchoolDataMethod != null)
                {
                    _harmony.Patch(initSchoolDataMethod,
                        postfix: new HarmonyMethod(typeof(Main), nameof(InitSchoolData_Postfix)));
                    Log("Harmony: InitSchoolData 补丁已应用");
                }
                else
                {
                    Log("警告：未找到 InitSchoolData 方法");
                }

                // ========== 补丁 2: Skill_Buff.Execute (修复冰河世纪 allEnemy 目标) ==========
                var skillBuffExecuteMethod = typeof(Skill_Buff).GetMethod("Execute",
                    BindingFlags.Public | BindingFlags.Instance);

                if (skillBuffExecuteMethod != null)
                {
                    _harmony.Patch(skillBuffExecuteMethod,
                        prefix: new HarmonyMethod(typeof(Main), nameof(SkillBuff_Execute_Prefix)));
                    Log("Harmony: Skill_Buff.Execute 前缀补丁已应用 (冰河世纪全屏冰冻)");
                }
                else
                {
                    Log("警告：未找到 Skill_Buff.Execute 方法");
                }

                // ========== 补丁 3: Skill_Buff.CreateBuffByType (修复霜寒冰冻持续回合加成) ==========
                var createBuffMethod = typeof(Skill_Buff).GetMethod("CreateBuffByType",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (createBuffMethod != null)
                {
                    _harmony.Patch(createBuffMethod,
                        postfix: new HarmonyMethod(typeof(Main), nameof(CreateBuffByType_Postfix)));
                    Log("Harmony: CreateBuffByType 后缀补丁已应用 (霜寒持续回合加成)");
                }
                else
                {
                    Log("警告：未找到 Skill_Buff.CreateBuffByType 方法");
                }

                // ========== 补丁 4: FreezeBuff.OnApply (修复绝对零度冰冻计数秒杀) ==========
                var freezeBuffOnApply = typeof(FreezeBuff).GetMethod("OnApply",
                    BindingFlags.Public | BindingFlags.Instance);

                if (freezeBuffOnApply != null)
                {
                    _harmony.Patch(freezeBuffOnApply,
                        postfix: new HarmonyMethod(typeof(Main), nameof(FreezeBuff_OnApply_Postfix)));
                    Log("Harmony: FreezeBuff.OnApply 后缀补丁已应用 (绝对零度冰冻计数)");
                }
                else
                {
                    Log("警告：未找到 FreezeBuff.OnApply 方法");
                }

                // ========== 补丁 5: UnitObject.UnitDead 前缀 (寒冰之心 + 连冻) ==========
                var unitDeadMethod = typeof(UnitObject).GetMethod("UnitDead",
                    BindingFlags.Public | BindingFlags.Instance);

                if (unitDeadMethod != null)
                {
                    _harmony.Patch(unitDeadMethod,
                        prefix: new HarmonyMethod(typeof(Main), nameof(UnitDead_Prefix)));
                    Log("Harmony: UnitObject.UnitDead 前缀补丁已应用 (寒冰之心 + 连冻)");
                }
                else
                {
                    Log("警告：未找到 UnitObject.UnitDead 方法");
                }

                // ========== 补丁 6: UnitObjectPlayer.TakeDamage 前缀 (寒霜额外伤害 + 寒冰反射) ==========
                // 注意：必须 Patch UnitObjectPlayer (子类) 而不是 UnitObject (基类)
                // 因为 UnitObjectPlayer 重写了 TakeDamage，有多个 early return (IFrame/无敌等)
                // 这些 early return 不会调用 base.TakeDamage()，导致基类 Patch 不触发
                var playerTakeDamageMethod = typeof(UnitObjectPlayer).GetMethod("TakeDamage",
                    BindingFlags.Public | BindingFlags.Instance);

                if (playerTakeDamageMethod != null)
                {
                    _harmony.Patch(playerTakeDamageMethod,
                        prefix: new HarmonyMethod(typeof(Main), nameof(PlayerTakeDamage_Prefix)));
                    Log("Harmony: UnitObjectPlayer.TakeDamage 前缀补丁已应用 (寒霜 + 寒冰反射)");
                }
                else
                {
                    Log("警告：未找到 UnitObjectPlayer.TakeDamage 方法");
                }

                // ========== 补丁 6b: UnitObjectAbility.TakeDamage 前缀 (寒霜额外伤害 - 敌人) ==========
                // 敌人由 UnitObjectAbility 管理，同样重写了 TakeDamage
                var abilityTakeDamageMethod = typeof(UnitObjectAbility).GetMethod("TakeDamage",
                    BindingFlags.Public | BindingFlags.Instance);

                if (abilityTakeDamageMethod != null)
                {
                    _harmony.Patch(abilityTakeDamageMethod,
                        prefix: new HarmonyMethod(typeof(Main), nameof(AbilityTakeDamage_Prefix)));
                    Log("Harmony: UnitObjectAbility.TakeDamage 前缀补丁已应用 (寒霜对敌人)");
                }
                else
                {
                    Log("警告：未找到 UnitObjectAbility.TakeDamage 方法");
                }

                // ========== 补丁 7: Buff.OnStack 后缀 (绝对零度计数器 + 冰冻刷新) ==========
                var buffOnStackMethod = typeof(Buff).GetMethod("OnStack",
                    BindingFlags.Public | BindingFlags.Instance);

                if (buffOnStackMethod != null)
                {
                    _harmony.Patch(buffOnStackMethod,
                        postfix: new HarmonyMethod(typeof(Main), nameof(Buff_OnStack_Postfix)));
                    Log("Harmony: Buff.OnStack 后缀补丁已应用 (绝对零度计数器 + 冰冻刷新)");
                }
                else
                {
                    Log("警告：未找到 Buff.OnStack 方法");
                }

                // ========== 补丁 8: UnitObjectPlayer.Action 后缀 (寒冰领域 DOT) ==========
                var playerActionMethod = typeof(UnitObjectPlayer).GetMethod("Action",
                    BindingFlags.Public | BindingFlags.Instance);

                if (playerActionMethod != null)
                {
                    _harmony.Patch(playerActionMethod,
                        postfix: new HarmonyMethod(typeof(Main), nameof(PlayerAction_IceDomain_Postfix)));
                    Log("Harmony: UnitObjectPlayer.Action 后缀补丁已应用 (寒冰领域)");
                }
                else
                {
                    Log("警告：未找到 UnitObjectPlayer.Action 方法");
                }

                // ========== 补丁 9: Skill_Buff.Execute 前缀 (冰甲护体 - 确保无敌给玩家) ==========
                if (skillBuffExecuteMethod != null)
                {
                    _harmony.Patch(skillBuffExecuteMethod,
                        prefix: new HarmonyMethod(typeof(Main), nameof(SkillBuff_Execute_IceArmor_Prefix)));
                    Log("Harmony: Skill_Buff.Execute 前缀补丁已应用 (冰甲护体玩家无敌)");
                }

                Log("所有 Harmony 补丁初始化完成");
            }
            catch (Exception ex)
            {
                Log($"Harmony 初始化失败：{ex.Message}");
                Log($"堆栈：{ex.StackTrace}");
            }
        }

        /// <summary>
        /// InitSchoolData 后置补丁 - 确保寒冰流派在幸运卡组选择时可用
        /// </summary>
        private static void InitSchoolData_Postfix(BattleObject __instance)
        {
            if (__instance.haveUnLockSchool == null)
            {
                __instance.haveUnLockSchool = new List<int>();
            }

            if (!__instance.haveUnLockSchool.Contains(IceSchoolId))
            {
                __instance.haveUnLockSchool.Add(IceSchoolId);
                Debug.Log($"[寒冰流派] InitSchoolData 后置补丁：已添加流派 {IceSchoolId} 到解锁列表");
            }
        }

        // ========================================================================
        // Harmony 补丁方法 - 冰冻机制实现
        // ========================================================================

        /// <summary>
        /// 补丁 1: Skill_Buff.Execute 前缀 - 修复冰河世纪全屏冰冻
        ///
        /// 冰河世纪 (1000260635): 每50回合冰冻所有敌人
        /// 游戏原生 Skill_Buff.Execute 不识别 allEnemy 目标，
        /// 使用 self 目标时 default 分支会冰冻玩家自身。
        /// 通过技能 ID 检测，对所有敌人施加冰冻效果，返回 false 跳过原始方法。
        /// </summary>
        private static bool SkillBuff_Execute_Prefix(Skill_Buff __instance, UnitObject target)
        {
            int id = __instance.skillConfig.id;
            // 只处理冰河世纪 (1000260635)
            if (id != 1000260635)
            {
                return true; // 其他技能继续执行原始方法
            }

            // 对所有敌人施加冰冻
            BattleObject bo = SingletonData<BattleObject>.Instance;
            if (bo != null && bo.Objects != null)
            {
                int frozenCount = 0;
                foreach (UnitObject unit in bo.Objects)
                {
                    if (unit == null || unit.hasDead) continue;
                    if (unit.unitCamp == UnitCamp.enemy || unit.unitCamp == UnitCamp.enemy2)
                    {
                        unit.buffManager.AddBuff(new FreezeBuff(unit));
                        frozenCount++;
                    }
                }
                Debug.Log($"[寒冰流派] 冰河世纪：对 {frozenCount} 个敌人施加冰冻");
            }

            return false; // 跳过原始方法，防止 default 分支冻结玩家
        }

        /// <summary>
        /// 补丁 9: Skill_Buff.Execute 前缀 - 冰甲护体 (确保无敌给玩家)
        ///
        /// 冰甲护体 (1000260625): hurt 触发器 target=攻击者，
        /// 但 paramName2="self" 应将无敌给玩家而非攻击者。
        /// 显式处理确保正确目标。
        /// </summary>
        private static bool SkillBuff_Execute_IceArmor_Prefix(Skill_Buff __instance, UnitObject target)
        {
            int id = __instance.skillConfig.id;
            if (id != 1000260625) return true; // 非冰甲护体，继续原始方法

            // 将无敌 Buff 施加给玩家（而非攻击者）
            BattleObject bo = SingletonData<BattleObject>.Instance;
            if (bo != null && bo.playerObject != null)
            {
                int duration = _iceArmorDuration > 0 ? _iceArmorDuration : 2;
                bo.playerObject.buffManager.AddBuff(new InvincibleBuff(bo.playerObject, duration));
                Debug.Log($"[寒冰流派] 冰甲护体：玩家获得 {duration} 回合无敌");
            }
            return false; // 跳过原始方法，防止无敌施加给攻击者
        }

        /// <summary>
        ///
        /// 游戏原生 FreezeBuff 硬编码 duration=7，且 duration 是 protected set 无法外部修改。
        /// 通过替换工厂方法返回值，使用 ExtendedFreezeBuff 子类支持自定义持续时间。
        /// </summary>
        public static void CreateBuffByType_Postfix(string type, UnitObject target, ref Buff __result)
        {
            if (type == "freezeBuff" && __result is FreezeBuff && _freezeTimeBonus > 0)
            {
                // 用 ExtendedFreezeBuff 替换原始 FreezeBuff，增加持续回合
                __result = new ExtendedFreezeBuff(target, 7 + _freezeTimeBonus);
            }
        }

        /// <summary>
        /// 补丁 3: FreezeBuff.OnApply 后缀 - 修复绝对零度冰冻计数秒杀
        ///
        /// 每次成功冰冻敌人时递增计数器。
        /// 当冰冻次数达到阈值 (默认 3 次) 时，造成大量伤害触发秒杀。
        /// 仅在玩家实际装备了绝对零度技能 (1000260632) 时生效。
        /// </summary>
        private static void FreezeBuff_OnApply_Postfix(FreezeBuff __instance, bool __result)
        {
            // 只处理成功施加的冰冻，且只追踪敌人
            if (!__result) return;
            if (__instance.owner == null) return;
            if (__instance.owner.unitCamp != UnitCamp.enemy &&
                __instance.owner.unitCamp != UnitCamp.enemy2) return;

            BattleObject bo = SingletonData<BattleObject>.Instance;
            if (bo == null || bo.playerObject == null) return;

            // ===== 绝对零度 (1000260632): 冰冻N次后秒杀 =====
            if (_freezeKillThreshold > 0 &&
                bo.playerObject.skillManager.GetSkill(1000260632) != null)
            {
                int unitId = RuntimeHelpers.GetHashCode(__instance.owner);

                // 递增冰冻计数
                if (!_freezeKillCounter.ContainsKey(unitId))
                {
                    _freezeKillCounter[unitId] = 0;
                }
                _freezeKillCounter[unitId]++;

                int count = _freezeKillCounter[unitId];
                Debug.Log($"[寒冰流派] 绝对零度：敌人冰冻次数 {count}/{_freezeKillThreshold}");

                // 达到阈值 → 秒杀
                if (count >= _freezeKillThreshold)
                {
                    Debug.Log($"[寒冰流派] 绝对零度：冰冻 {count} 次触发秒杀！");
                    __instance.owner.TakeDamage(9999, __instance.owner);
                    _freezeKillCounter[unitId] = 0;
                }
            }

            // ===== 冰魂 (1000260626): 冰冻敌人时回复 HP (多个冰魂叠加) =====
            int iceSoulCount = GetIceSoulSkillCount();
            if (iceSoulCount > 0)
            {
                int totalHeal = _iceSoulHeal * iceSoulCount;
                bo.AddHP(totalHeal);
                Debug.Log($"[寒冰流派] 冰魂：{iceSoulCount} 个冰魂叠加，回复 {totalHeal} HP");
            }
        }

        /// <summary>
        /// 补丁 4: UnitObject.UnitDead 前缀 - 实现连冻效果
        ///
        /// 必须用 Prefix 而非 Postfix，因为 UnitDead 内部调用 HandleDead() 会
        /// 清空所有 buff (buffManager.ClearAll())。Postfix 执行时冰冻状态已丢失。
        ///
        /// 连冻 (1000260627): 冰冻敌人死亡时，冰冻周围 spreadRange 格敌人
        /// </summary>
        private static void UnitDead_Prefix(UnitObject __instance, UnitObject murder)
        {
            if (_spreadRange <= 0) return;
            if (__instance == null) return;

            // 只处理敌人
            if (__instance.unitCamp != UnitCamp.enemy && __instance.unitCamp != UnitCamp.enemy2) return;

            // 检查该敌人死亡时是否处于冰冻状态 (此时 buff 尚未被 ClearAll 清空)
            if (!__instance.buffManager.HasBuff(BuffType.Freeze)) return;
            if (__instance.unitPos == default) return;

            BattleObject bo = SingletonData<BattleObject>.Instance;
            if (bo == null || bo.Objects == null) return;

            // 连冻：冰冻敌人死亡，冰冻周围 spreadRange 格敌人
            int spreadCount = 0;
            foreach (UnitObject unit in bo.Objects)
            {
                if (unit == null || unit.hasDead) continue;
                if (unit == __instance) continue;
                if (unit.unitCamp != UnitCamp.enemy && unit.unitCamp != UnitCamp.enemy2) continue;
                if (unit.buffManager.HasBuff(BuffType.Freeze)) continue; // 已经冰冻的跳过
                if (Vector2Int.Distance(unit.unitPos, __instance.unitPos) <= _spreadRange)
                {
                    unit.buffManager.AddBuff(new FreezeBuff(unit));
                    spreadCount++;
                }
            }
            if (spreadCount > 0)
            {
                Debug.Log($"[寒冰流派] 连冻：冰冻扩散到 {spreadCount} 个敌人");
            }
        }

        /// <summary>
        /// 补丁 6: UnitObjectPlayer.TakeDamage 前缀 - 寒冰反射
        ///
        /// 必须 Patch UnitObjectPlayer (子类) 而不是 UnitObject (基类)。
        /// 因为 UnitObjectPlayer 重写了 TakeDamage，有多个 early return：
        /// - IFrame 闪避帧 (dodgeParryIFrameTimer > 0)
        /// - 无敌状态 (Invincible buff)
        /// - 特定敌人类型免疫
        /// 这些 early return 不会调用 base.TakeDamage()，导致基类 Patch 不触发。
        /// </summary>
        private static void PlayerTakeDamage_Prefix(UnitObjectPlayer __instance, int damge, UnitObject atkUnit)
        {
            if (__instance == null || atkUnit == null) return;

            // ===== 寒冰反射 (1000260629): 玩家受伤时冰冻攻击者 =====
            bool isEnemy = (atkUnit.unitCamp == UnitCamp.enemy || atkUnit.unitCamp == UnitCamp.enemy2);
            bool notDead = !atkUnit.hasDead;

            if (isEnemy && notDead)
            {
                var skill = __instance.skillManager.GetSkill(1000260629);
                if (skill != null)
                {
                    Buff buff = _freezeTimeBonus > 0
                        ? (Buff)new ExtendedFreezeBuff(atkUnit, 7 + _freezeTimeBonus)
                        : new FreezeBuff(atkUnit);

                    atkUnit.buffManager.AddBuff(buff);
                }
            }
        }

        /// <summary>
        /// 补丁 6b: UnitObjectAbility.TakeDamage 前缀 - 寒霜额外伤害 (敌人)
        ///
        /// 敌人由 UnitObjectAbility 管理，同样重写了 TakeDamage。
        /// 必须 Patch 子类才能在所有情况下触发。
        /// </summary>
        private static void AbilityTakeDamage_Prefix(UnitObjectAbility __instance, ref int damge, UnitObject atkUnit)
        {
            if (_frozenAtkBonus <= 0) return;
            if (__instance == null || atkUnit == null) return;
            if (__instance.unitCamp != UnitCamp.enemy && __instance.unitCamp != UnitCamp.enemy2) return;
            if (atkUnit.unitCamp != UnitCamp.player) return;
            if (!__instance.buffManager.HasBuff(BuffType.Freeze)) return;

            damge += _frozenAtkBonus;
        }

        /// <summary>
        /// 补丁 6: Buff.OnStack 后缀 - 绝对零度计数器 (已冰冻敌人再次被冰冻)
        ///
        /// 当敌人已处于冰冻状态时，BuffManager.AddBuff 走 OnStack 分支而非 OnApply。
        /// 在此补丁中递增绝对零度计数器，并刷新冰冻持续时间。
        /// </summary>
        private static void Buff_OnStack_Postfix(Buff __instance, Buff newBuff)
        {
            // 只处理冰冻 Buff
            if (__instance.Type != BuffType.Freeze) return;
            if (__instance.owner == null) return;

            // 绝对零度：递增冰冻计数器
            if (__instance.owner.unitCamp == UnitCamp.enemy || __instance.owner.unitCamp == UnitCamp.enemy2)
            {
                if (_freezeKillThreshold > 0)
                {
                    // 检查玩家是否装备了绝对零度技能
                    BattleObject bo = SingletonData<BattleObject>.Instance;
                    if (bo != null && bo.playerObject != null &&
                        bo.playerObject.skillManager.GetSkill(1000260632) != null)
                    {
                        int unitId = RuntimeHelpers.GetHashCode(__instance.owner);
                        if (!_freezeKillCounter.ContainsKey(unitId))
                            _freezeKillCounter[unitId] = 0;
                        _freezeKillCounter[unitId]++;

                        int count = _freezeKillCounter[unitId];
                        Debug.Log($"[寒冰流派] 绝对零度 (OnStack)：敌人冰冻次数 {count}/{_freezeKillThreshold}");

                        if (count >= _freezeKillThreshold)
                        {
                            Debug.Log($"[寒冰流派] 绝对零度 (OnStack)：冰冻 {count} 次触发秒杀！");
                            __instance.owner.TakeDamage(9999, __instance.owner);
                            _freezeKillCounter[unitId] = 0;
                        }
                    }
                }
            }

            // ===== 冰魂 (1000260626): 再次冰冻敌人时也回复 HP (多个冰魂叠加) =====
            {
                int iceSoulCountStack = GetIceSoulSkillCount();
                if (iceSoulCountStack > 0)
                {
                    int totalHealStack = _iceSoulHeal * iceSoulCountStack;
                    BattleObject boHeal = SingletonData<BattleObject>.Instance;
                    if (boHeal != null && boHeal.playerObject != null)
                    {
                        boHeal.AddHP(totalHealStack);
                        Debug.Log($"[寒冰流派] 冰魂 (OnStack)：{iceSoulCountStack} 个冰魂叠加，回复 {totalHealStack} HP");
                    }
                }
            }

            // 刷新冰冻持续时间 (已冰冻敌人再次被冰冻时，重置回合数)
            if (__instance is ExtendedFreezeBuff extBuff)
            {
                // ExtendedFreezeBuff 暴露了 RefreshDuration 方法
                int baseDuration = 7 + _freezeTimeBonus;
                if (baseDuration > 0) extBuff.RefreshDuration(baseDuration);
            }
            else
            {
                // 普通 FreezeBuff 替换为 ExtendedFreezeBuff 以支持自定义持续时间
                int newDuration = 7 + _freezeTimeBonus;
                if (newDuration > 7)
                {
                    var buffManager = __instance.owner.buffManager;
                    buffManager.RemoveBuff(BuffType.Freeze);
                    buffManager.AddBuff(new ExtendedFreezeBuff(__instance.owner, newDuration));
                }
            }
        }

        /// <summary>
        /// 游戏开始时调用 - 读取被动技能值并重置计数器
        /// </summary>
        private void OnGameStart(BattleObject bo)
        {
            Log("游戏开始，确保流派已解锁");
            AddSchoolToUnlockList();

            // 重置冰冻计数器
            _freezeKillCounter.Clear();
            _lastIceDomainActionTurn = -1;

            // 读取被动技能值 (这些 paramName 在游戏引擎中不存在，需要手动读取)
            ReadPassiveSkillValues();
        }

        /// <summary>
        /// 补丁 8: UnitObjectPlayer.Action 后缀 - 寒冰领域 DOT
        ///
        /// 每次玩家单位执行 Action() 时检查是否已触发过本回合效果。
        /// 直接扣血 (不使用 TakeDamage)，避免触发寒霜额外伤害和击退效果。
        /// HP 归零时手动调用 UnitDead 处理死亡。
        /// </summary>
        private static void PlayerAction_IceDomain_Postfix(UnitObjectPlayer __instance)
        {
            if (_iceDomainDamage <= 0) return;
            // 每回合只触发一次 (通过 actionTurn 判断)
            int currentTurn = __instance.actionTurn;
            if (currentTurn == _lastIceDomainActionTurn) return;

            BattleObject bo = SingletonData<BattleObject>.Instance;
            if (bo == null || bo.playerObject == null) return;
            // 检查玩家是否装备了寒冰领域
            if (bo.playerObject.skillManager.GetSkill(1000260630) == null) return;
            if (bo.Objects == null) return;

            _lastIceDomainActionTurn = currentTurn;

            int hitCount = 0;
            foreach (UnitObject target in bo.Objects)
            {
                if (target == null || target.hasDead) continue;
                if (target.unitCamp != UnitCamp.enemy && target.unitCamp != UnitCamp.enemy2) continue;
                if (!target.buffManager.HasBuff(BuffType.Freeze)) continue;

                // 直接扣血，不触发 TakeDamage (避免寒霜叠加和击退)
                target.unitHP -= _iceDomainDamage;

                if (target.unitHP <= 0)
                {
                    target.unitHP = 0;
                    target.UnitDead(bo.playerObject);
                }
                hitCount++;
            }
            if (hitCount > 0)
            {
                Debug.Log($"[寒冰流派] 寒冰领域：对 {hitCount} 个冰冻敌人造成 {_iceDomainDamage} 点伤害");
            }
        }

        /// <summary>
        /// 从 SkillConfigLoader 读取寒冰流派的被动技能参数值
        /// </summary>
        private void ReadPassiveSkillValues()
        {
            // 霜寒 (1000260622): freezeTime=5 → 冰冻持续回合+5
            SkillConfig freezeTimeConfig = SkillConfigLoader.GetConfig(1000260622);
            if (freezeTimeConfig != null)
            {
                _freezeTimeBonus = Mathf.RoundToInt(freezeTimeConfig.param1);
                Log($"霜寒: 冰冻持续回合加成 = +{_freezeTimeBonus}");
            }

            // 绝对零度 (1000260632): freezeKillCount=3 → 冰冻3次后秒杀
            SkillConfig freezeKillConfig = SkillConfigLoader.GetConfig(1000260632);
            if (freezeKillConfig != null)
            {
                _freezeKillThreshold = Mathf.RoundToInt(freezeKillConfig.param1);
                Log($"绝对零度: 冰冻秒杀阈值 = {_freezeKillThreshold} 次");
            }

            // 连冻 (1000260627): spreadRange=1 → 冰冻敌人死亡时冰冻周围1格
            SkillConfig spreadRangeConfig = SkillConfigLoader.GetConfig(1000260627);
            if (spreadRangeConfig != null)
            {
                _spreadRange = Mathf.RoundToInt(spreadRangeConfig.param1);
                Log($"连冻: 冰冻扩散范围 = {_spreadRange} 格");
            }

            // 寒霜 (1000260633): frozenAtkBonus=2 → 攻击冰冻敌人时伤害+2
            SkillConfig frozenAtkConfig = SkillConfigLoader.GetConfig(1000260633);
            if (frozenAtkConfig != null)
            {
                _frozenAtkBonus = Mathf.RoundToInt(frozenAtkConfig.param1);
                Log($"寒霜: 冰冻敌人额外伤害 = +{_frozenAtkBonus}");
            }

            // 冰魂 (1000260626): iceSoulHeal=2 → 冰冻敌人时回复 2 HP
            SkillConfig iceSoulConfig = SkillConfigLoader.GetConfig(1000260626);
            if (iceSoulConfig != null)
            {
                _iceSoulHeal = Mathf.RoundToInt(iceSoulConfig.param1);
                Log($"冰魂: 单次冰冻回复 HP = {_iceSoulHeal}");
            }

            // 寒冰领域 (1000260630): iceDomainDamage=1 → 每回合对冰冻敌人造成 1 点伤害
            SkillConfig iceDomainConfig = SkillConfigLoader.GetConfig(1000260630);
            if (iceDomainConfig != null)
            {
                _iceDomainDamage = Mathf.RoundToInt(iceDomainConfig.param1);
                Log($"寒冰领域: 每回合冰冻敌人伤害 = {_iceDomainDamage}");
            }

            // 冰甲护体 (1000260625): param2=2 → 无敌持续 2 回合
            SkillConfig iceArmorConfig = SkillConfigLoader.GetConfig(1000260625);
            if (iceArmorConfig != null)
            {
                _iceArmorDuration = Mathf.RoundToInt(iceArmorConfig.param2);
                if (_iceArmorDuration <= 0) _iceArmorDuration = 2; // 默认 2 回合
                Log($"冰甲护体: 无敌持续 = {_iceArmorDuration} 回合");
            }
        }

        /// <summary>
        /// 获取玩家装备的冰魂技能数量 (用于叠加回血)
        /// activeSkills 是 SkillManager 的私有字段，需通过反射访问
        /// </summary>
        private static int GetIceSoulSkillCount()
        {
            if (_iceSoulHeal <= 0) return 0;
            BattleObject bo = SingletonData<BattleObject>.Instance;
            if (bo == null || bo.playerObject == null || bo.playerObject.skillManager == null) return 0;

            try
            {
                var skillsField = typeof(SkillManager).GetField("activeSkills",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (skillsField == null) return 0;

                var skills = skillsField.GetValue(bo.playerObject.skillManager) as System.Collections.IList;
                if (skills == null) return 0;

                int count = 0;
                foreach (Skill skill in skills)
                {
                    if (skill != null && skill.skillData != null && skill.skillData.id == 1000260626)
                        count++;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 关卡开始时调用
        /// </summary>
        private void OnLevelStart(BattleObject bo)
        {
            // 可以在这里添加关卡特定的逻辑
        }

        /// <summary>
        /// 主菜单数据加载后调用
        /// </summary>
        private void OnAfterHomeDataLoad(BattleObject bo)
        {
            // 确保主菜单中也能看到流派
            AddSchoolToUnlockList();
        }

        /// <summary>
        /// 注册寒冰技能配置
        /// </summary>
        private void RegisterIceSkills()
        {
            runtimeSkillConfigIds.Clear();

            // 加载 CSV 配置
            LoadConfigsFromCSV();

            Log($"已注册 {runtimeSkillConfigIds.Count} 个寒冰技能");
        }

        /// <summary>
        /// 从 CSV 文件加载技能配置
        /// </summary>
        private void LoadConfigsFromCSV()
        {
            // 技能配置会通过游戏原生的 ModSkillConfigs.csv 加载机制自动加载
            // 这里只需要确保 ID 被记录
            for (int i = 0; i < SkillCount; i++)
            {
                int skillId = SkillBaseId + i;
                if (!runtimeSkillConfigIds.Contains(skillId))
                {
                    runtimeSkillConfigIds.Add(skillId);
                }
            }
        }

        /// <summary>
        /// 添加流派到解锁列表
        /// </summary>
        private void AddSchoolToUnlockList()
        {
            BattleObject bo = SingletonData<BattleObject>.Instance;
            if (bo == null) return;

            if (bo.haveUnLockSchool == null)
            {
                bo.haveUnLockSchool = new List<int>();
            }

            if (!bo.haveUnLockSchool.Contains(IceSchoolId))
            {
                bo.haveUnLockSchool.Add(IceSchoolId);
                Log($"已添加流派 {IceSchoolId} 到解锁列表");
            }
        }

        /// <summary>
        /// 移除运行时配置
        /// </summary>
        private void RemoveRuntimeConfigs()
        {
            // 重新加载配置会清除运行时添加的技能
            SkillConfigLoader.ReloadConfigs();
            Log("已移除运行时配置");
        }

        /// <summary>
        /// 日志输出
        /// </summary>
        private new void Log(string msg)
        {
            Debug.Log($"{LogPrefix} {msg}");
        }
    }

    /// <summary>
    /// 扩展冰冻 Buff - 支持自定义持续回合 + 绕过大体型冰冻免疫
    ///
    /// 游戏原生 FreezeBuff 有两个限制：
    /// 1. 构造函数硬编码 duration=7
    /// 2. OnApply() 对 unitScale > 1 的大体型敌人有 20 回合冰冻免疫
    ///    (lastFreezeTurn < 20 时 return false，导致 Buff 不加入 BuffManager)
    ///
    /// 此子类：
    /// - 允许指定任意 duration（用于霜寒加成）
    /// - 覆盖 OnApply() 绕过大体型免疫检查（用于寒冰反射 100% 触发）
    /// </summary>
    public class ExtendedFreezeBuff : FreezeBuff
    {
        public ExtendedFreezeBuff(UnitObject owner, int duration)
            : base(owner, duration)
        {
            // FreezeBuff 构造函数已经设置了 BuffType.Freeze
        }

        /// <summary>
        /// 刷新冰冻持续时间 (duration 是 protected set，需要子类方法暴露)
        /// </summary>
        public void RefreshDuration(int newDuration)
        {
            this.duration = newDuration;
        }

        /// <summary>
        /// 覆盖 OnApply() - 绕过大体型冰冻免疫
        ///
        /// 原生 FreezeBuff.OnApply() 对 unitScale > 1 的敌人检查 lastFreezeTurn < 20，
        /// 导致冰冻失败 (Buff 不加入 BuffManager)。
        /// 我们直接重置 lastFreezeTurn = 20 满足条件，并手动添加冰冻特效。
        /// </summary>
        public override bool OnApply()
        {
            // 先调用原生 OnApply 尝试正常流程
            bool result = base.OnApply();

            if (!result && owner != null)
            {
                // 原生 OnApply 失败 (可能是大体型免疫)
                // 强制绕过：重置 lastFreezeTurn 使检查通过
                if (owner.unitScale > 1 || owner.unitCamp == UnitCamp.player)
                {
                    owner.lastFreezeTurn = 20; // 满足 >= 20 条件
                    result = base.OnApply();   // 再次尝试
                }
            }

            return result;
        }
    }
}
