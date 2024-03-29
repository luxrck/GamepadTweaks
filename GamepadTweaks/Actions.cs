using Dalamud;
using Dalamud.Game.ClientState;
using Dalamud.Logging;
using Newtonsoft.Json;

using System.Reflection;
using System.Runtime.InteropServices;

using FFXIVClientStructs.FFXIV.Client.Game;

namespace GamepadTweaks
{
    public enum ActionUseType : uint
    {
        User = 0,
        AutoGenerated = 1,
        Macro = 2,
    }

    // public enum ActionType : uint
    // {
    //     Spell = 0x01,
    //     Ability = 0x04,
    // }

    // ac1 ac2 b1 d1
    // ac1 -> [animation : Locking] -> [before next gcd : Pending] -> []
    public enum ActionStatus : uint
    {
        Ready = 0,
        Unk_570 = 570,
        Unk_571 = 571,
        NotSatisfied = 572,
        NotLearned = 573,
        Unk_574 = 574,
        // 咏唱时间内
        Locking = 580,
        Unk_581 = 581,
        // 咏唱时间段外, 复唱时间内
        Pending = 582,

        // 自定义
        Delay = 0xffff0001,
        LocalDelay = 0xffff0002,
        StateUpdate = 0xffff0003,
        Invalid = 0xffffffff,
    }

    public class GameAction {
        // public IntPtr ActionManager = IntPtr.Zero;
        public uint ID;
        public ActionType Type = ActionType.Spell;

        public uint TargetID = Configuration.DefaultInvalidGameObjectID;
        public uint param = 0;
        public uint UseType = 0;
        public uint pvp = 0;
        public IntPtr a8 = IntPtr.Zero;

        public ActionStatus Status = ActionStatus.Ready;
        public DateTime LastTime = DateTime.Now;

        public ActionInfo? Info => Plugin.Actions[ID];

        public string Name => Info?.Name ?? String.Empty;
        // public Dictionary<string, string> Names = new Dictionary<string, string>();

        // public DateTime DelayTo = DateTime.Now;

        public bool HasTarget => TargetID != Configuration.DefaultInvalidGameObjectID;
        public bool IsTargetingSelf => TargetID == (Plugin.Player?.ObjectId ?? 0);
        public bool IsTargetingPartyMember => IsTargetingSelf || Plugin.GamepadActionManager.GetSortedPartyMembers().Any(x => x.ID == TargetID);
        public bool IsValid => this.ID > 0;
        public bool IsCasting => Plugin.Player is not null && Plugin.Player.IsCasting && Plugin.Actions.Equals(Plugin.Player.CastActionId, ID);

        // 由于即刻咏唱等的存在, CastTime必须动态获取
        // TODO: Should get Action cast detail info even not in casting.
        public int CastTimeTotalMilliseconds => Plugin.Actions.Cooldown(ID, adjusted: false);
        public int AdjustedCastTimeTotalMilliseconds => Plugin.Actions.CastTime(ID, adjusted: true);
        public int AdjustedReastTimeTotalMilliseconds => Plugin.Actions.RecastTime(ID);

        public bool CanCastImmediatly => AdjustedCastTimeTotalMilliseconds == 0;
        public bool Finished = false;

        public async Task<bool> Wait()
        {
            if (!Plugin.Ready || Plugin.Player is null) return false;
            if (Finished || Status != ActionStatus.Ready) return true;

            // 有些技能, 咏唱时间大于gcd(例如红宝石灾祸). 如果卡着咏唱结束时间连点的话,
            // 游戏会连续释放2次UseAction[status==Ready] ???
            // UseAction[a,Ready,UseType==0] : me.IsCasting == false -> UseAction[a,Ready,UseType==1] : me.IsCasting == true
            // 由于第一次UseAction时, IsCasting == false. 因此Finished == true && status == Ready, 函数会立即返回true.
            // TODO: GetAdjustedCastTime 有问题?

            var me = Plugin.Player;

            if (me.IsCasting) {
                if (Plugin.Actions.Equals(me.CastActionId, ID)) {
                    var start = (int)(me.CurrentCastTime * 1000);
                    var total = (int)(me.TotalCastTime * 1000);

                    var delay = 10;

                    // 实际上最后四分之一个GCD内完成casting时,技能都会生效.
                    // 设置一个误差, 让处理不那么严格.
                    var eps = 200;

                    var current = start;

                    if (total - current < Configuration.GlobalCoolingDown.SlidingWindow) return true;

                    // 滑步!!!
                    var before = DateTime.Now;
                    while (current < total) {
                        await Task.Delay(delay);
                        if (me.CurrentCastTime == 0 || me.TotalCastTime == 0) break;
                        if (me.CurrentCastTime >= me.TotalCastTime) break;
                        if (!me.IsCasting) break;
                        current += delay;
                    }
                    var after = DateTime.Now;
                    PluginLog.Debug($"[Casting] action: {Plugin.Actions.Name(ID)}, start: {start}, total: {total} {(after - before).TotalMilliseconds}");
                    var passed = (after - before).TotalMilliseconds;
                    if (passed >= (total - start - eps)) Finished = true;
                    // await Task.Delay((int)passed - total + start + eps);
                    await Task.Delay(Math.Max(total - start - eps - (int)passed, 0));
                    return Finished;
                } else {
                    // why reach here ???
                    // abbbb[bc]. bc成功,且都需要咏唱.
                    // UseAction[b], b is casting -> Put(b) -> UseAction[c], c is casting -> UpdateState(b) -> Put(c) -> UpdteState(c)
                    return false;
                }
            } else {
                return false;
                // if (AdjustedCastTimeTotalMilliseconds > 0) {
                //     // 应该咏唱但是确没有, 被打断了?
                //     // 目前StateUpdate等待200ms, 不会超过AnimationLock的时间.
                //     // StateUpdate(0)是不会阻塞的.
                //     // 因此一个正常的Action不应该会由于在队列里等待过久而造成中间状态更新了
                //     if ((DateTime.Now - LastTime).TotalMilliseconds < CastTimeTotalMilliseconds) {
                //         await Task.Delay((int)(AdjustedCastTimeTotalMilliseconds - Configuration.GlobalCoolingDown.SlidingWindow));
                //         return true;
                //     } else {
                //         // 真要这样那只能丢弃了
                //         return false;
                //     }
                // } else {
                //     return true;
                // }
            }
        }
    }

    // From: https://github.com/UnknownX7/NoClippy/Structures/ActionManager.cs
    // [StructLayout(LayoutKind.Explicit)]
    // public struct ActionManagerFields
    // {
    //     [FieldOffset(0x8)] public float AnimationLock;
    //     [FieldOffset(0x28)] public bool IsCasting;
    //     [FieldOffset(0x30)] public float ElapsedCastTime;
    //     [FieldOffset(0x34)] public float CastTime;
    //     [FieldOffset(0x60)] public float RemainingComboTime;
    //     [FieldOffset(0x68)] public bool IsQueued;
    //     [FieldOffset(0x110)] public ushort CurrentSequence;
    //     [FieldOffset(0x112)] public ushort LastReceivedSequence;
    //     [FieldOffset(0x610)] public bool IsGCDRecastActive;
    //     [FieldOffset(0x614)] public uint CurrentGCDAction;
    //     [FieldOffset(0x618)] public float ElapsedGCDRecastTime;
    //     [FieldOffset(0x61C)] public float GCDRecastTime;
    // }

    public class ActionInfo
    {
        public uint ID;
        public string Name => Names.ContainsKey(Plugin.ClientLanguage) ? Names[Plugin.ClientLanguage] : String.Empty;
        public Dictionary<string, string> Names = new Dictionary<string, string>();
        public int Icon;
        public int ActionCategory;
        public int ClassJob;
        public int BehaviourType;
        public int ClassJobLevel;
        public bool IsRoleAction;
        public int Range;
        public bool CanTargetSelf;
        public bool CanTargetParty;
        public bool CanTargetFriendly;
        public bool CanTargetHostile;
        public bool TargetArea;
        public bool CanTargetDea;
        public int CastType;
        public int EffectRange;
        public int XAxisModifier;
        // public Action{Combo}
        public bool PreservesCombo;
        public int CastTime;
        public int RecastTime;
        public int CooldownGroup;
        public int AdditionalCooldownGroup;
        public int MaxCharges;
        public int AttackType;
        public int Aspect;
        public int ActionProcStatus;
        // public Status{GainSelf}
        public int UnlockLink;
        public int ClassJobCategory;
        public bool AffectsPosition;
        public int Omen;
        public bool IsPvP;
        public bool IsPlayerAction;
    }

    public class Actions
    {
        private Dictionary<uint, uint> AliasMap = new Dictionary<uint, uint>();
        private Dictionary<uint, ActionInfo> InfoMap = new Dictionary<uint, ActionInfo>();
        private Dictionary<string, Dictionary<string, uint>> NameMap = new Dictionary<string, Dictionary<string, uint>>();

        public Actions()
        {
            try {
                PluginLog.Debug($"Load Action Info Data: {Configuration.ActionFile}");
                var content = File.ReadAllText(Configuration.ActionFile);
                var infos = JsonConvert.DeserializeObject<List<ActionInfo>>(content) ?? new List<ActionInfo>();

                var langs = new string[] {"zh", "en", "jp", "fr", "de"};

                foreach(var lang in langs) {
                    NameMap[lang] = new Dictionary<string, uint>();
                }

                foreach (var info in infos) {
                    if (!InfoMap.ContainsKey(info.ID)) {
                        InfoMap[info.ID] = info;
                    }

                    foreach (var name in info.Names) {
                        if (!NameMap[name.Key].ContainsKey(name.Value)) {
                            NameMap[name.Key][name.Value] = info.ID;
                        } else {
                            PluginLog.Warning($"Duplicate actions: {info.Name}. action id: {NameMap[name.Key][name.Value]} already exists while incoming {info.ID}");
                        }
                    }
                }

                PluginLog.Debug($"Load Action Alias Data: {Configuration.AliasFile}");
                var alias = File.ReadAllText(Configuration.AliasFile);
                BuildAliasInfo(alias);
                TestAliasInfo();

            } catch(Exception e) {
                PluginLog.Error($"Exception: {e}");
            }
        }

        private void BuildAliasInfo(string alias)
        {
            var previousRoot = 0u;
            var lines = alias.Split("\n").Select(x => x.Trim()).Where(x => !String.IsNullOrEmpty(x)).ToList();
            foreach (var line in lines) {

                if (line.StartsWith("#") || line.StartsWith("//")) {
                    continue;
                }

                var components = line.Split("<-", 2);
                var root = components[0].Trim();
                var children = components[1].Trim();

                var rootID = ID(root, lang: "zh");

                if (rootID == 0) rootID = previousRoot;
                if (rootID == 0) {
                    PluginLog.Warning($"[BuildAliasInfo] No root?: {line}");
                    continue;
                }

                if (children.StartsWith("{") && children.EndsWith("}")) {
                    var nodes = children[1..^1].Split(",").Select(x => x.Trim()).Where(x => !String.IsNullOrEmpty(x));
                    foreach (var node in nodes) {
                        var nodeID = ID(node, lang: "zh");
                        if (nodeID != 0) {
                            AliasMap[nodeID] = rootID;
                        } else {
                            PluginLog.Warning($"[BuildAliasIndo] Invalid action: {node}");
                        }
                    }
                } else {
                    var previousNode = rootID;
                    var nodes = children.Split("<-").Select(x => x.Trim()).Where(x => !String.IsNullOrEmpty(x));
                    foreach (var node in nodes) {
                        var nodeID = ID(node, lang: "zh");
                        if (nodeID != 0) {
                            AliasMap[nodeID] = previousNode;
                            previousNode = nodeID;
                        } else {
                            PluginLog.Warning($"[BuildAliasIndo] Invalid action: {node}");
                        }
                    }
                }

                AliasMap[rootID] = 0;
                previousRoot = rootID;
            }
        }

        private void TestAliasInfo()
        {
            foreach (var i in AliasMap) {
                PluginLog.Debug($"{Name(i.Value)} <- {Name(i.Key)}");
            }
        }

        private static Actions instance = null!;
        private static readonly object instanceLock = new object();
        public static Actions Instance()
        {
            lock(instanceLock) {
                if (instance == null) {
                    instance = new Actions();
                }
                return instance;
            }
        }

        public bool Contains(string action, string lang = "auto") => NameMap[lang == "auto" ? Plugin.ClientLanguage : lang].ContainsKey(action);
        public bool Contains(uint action) => InfoMap.ContainsKey(action);

        public bool Equals(uint a1, uint a2) => IsSameAction(a1, a2);
        public bool IsSameAction(uint a1, uint a2)
        {
            if (a1 == a2) return true;
            if (!AliasMap.ContainsKey(a1) || !AliasMap.ContainsKey(a2)) return false;

            var n = a1;
            while (AliasMap.ContainsKey(n)) {
                if (n == a2) return true;
                n = AliasMap[n];
            }

            n = a2;
            while (AliasMap.ContainsKey(n)) {
                if (n == a1) return true;
                n = AliasMap[n];
            }

            return false;
        }
        public bool InSameAliasGroup(uint a1, uint a2)
        {
            while (AliasMap.ContainsKey(a1)) a1 = AliasMap[a1];
            while (AliasMap.ContainsKey(a2)) a2 = AliasMap[a2];
            return a1 == a2;
        }

        public ActionInfo? this[string a, string lang = "auto"] => Contains(a, lang) ? InfoMap[NameMap[lang == "auto" ? Plugin.ClientLanguage : lang][a]] : null;
        public ActionInfo? this[uint a] => Contains(a) ? InfoMap[a] : null;
        public uint ID(string a, string lang = "auto") => this[a, lang]?.ID ?? 0;
        public string Name(uint i) => this[i]?.Name ?? String.Empty;

        public uint BaseActionID(uint actionID) => AliasMap.ContainsKey(actionID) ? AliasMap[actionID] : actionID;
        public FFXIVClientStructs.FFXIV.Client.Game.ActionType ActionType(uint actionID) => FFXIVClientStructs.FFXIV.Client.Game.ActionType.Spell;

        public uint AdjustedActionID(uint actionID)
        {
            var adjustedID = actionID;
            unsafe {
                var am = ActionManager.Instance();
                if (am != null) {
                    // real action id
                    adjustedID = am->GetAdjustedActionId(actionID);
                }
            }
            return adjustedID;
        }

        // ActionType.Spell == 0x01;
        // ActionType.Ability == 0x04;
        public ActionStatus ActionStatus(uint actionID, ActionType actionType = 0U, uint targetedActorID = 3758096384U)
        {
            uint status = 0;
            unsafe {
                var am = ActionManager.Instance();
                if (am != null) {
                    var at = actionType > 0 ? actionType : ActionType(actionID);
                    status = am->GetActionStatus(at, actionID, targetedActorID);
                }
            }

            return (GamepadTweaks.ActionStatus)status;
        }

        public int CastTime(uint actionID, bool adjusted = true)
        {
            unsafe {
                return ActionManager.GetAdjustedCastTime(ActionType(actionID), actionID);
            }
        }

        // Auto Attack, DoT and HoT Strength:
        //      f(SPD) = ( 1000 + ⌊ 130 × ( SS - Level Lv, SUB ) / Level Lv, DIV ⌋ ) / 1000
        // Weaponskill and Spell Cast and Global Cooldown Reduction (No Haste Buffs):
        //      f(GCD) = ⌊ ((GCD * (1000 + ⌈ 130 × ( Level Lv, SUB - Speed)/ Level Lv, DIV)⌉ ) / 10000) / 100 ⌋

        // GCD Calculation (5.x)
        // GCD1 = ⌊ ( 2000 - f(SPD) ) × Action Delay / 1000 ⌋
        // GCD2 = ⌊ ( 100 - ∑ Speed Buffs ) × ( 100 - Haste ) / 100 ⌋
        // GCD3 = ⌊ GCD2 × GCD1 / 1000 ⌋ × Astral_Umbral / 100 ⌋
        // Final GCD = GCD3 / 100
        //
        // Speed Buffs
        // Name    Value
        // Ley Lines   15
        // Presence of Mind    20
        // Shifu   13
        // Blood Weapon    10
        // Huton   15
        // *Greased Lightning 1/2/3/4   5, 10, 15, 20
        // *Repertoire 1/2/3/4  4, 8, 12, 16
        public int Cooldown(uint actionID, bool adjusted = true)
        {
            // Lv.0 ~ Lv.90
            var SUB = new uint[] {55, 56, 57, 60, 62, 65, 68, 70, 73, 76, 78, 82, 85, 89, 93, 96, 100, 104, 109, 113, 116, 122, 127, 133, 138, 144, 150, 155, 162, 168, 173, 181, 188, 194, 202, 209, 215, 223, 229, 236, 244, 253, 263, 272, 283, 292, 302, 311, 322, 331, 341, 342, 344, 345, 346, 347, 349, 350, 351, 352, 354, 355, 356, 357, 358, 359, 360, 361, 362, 363, 364, 365, 366, 367, 368, 370, 372, 374, 376, 378, 380, 382, 384, 386, 388, 390, 392, 394, 396, 398, 400};
            var DIV = new uint[] {55, 56, 57, 60, 62, 65, 68, 70, 73, 76, 78, 82, 85, 89, 93, 96, 100, 104, 109, 113, 116, 122, 127, 133, 138, 144, 150, 155, 162, 168, 173, 181, 188, 194, 202, 209, 215, 223, 229, 236, 244, 253, 263, 272, 283, 292, 302, 311, 322, 331, 341, 366, 392, 418, 444, 470, 496, 522, 548, 574, 600, 630, 660, 690, 720, 750, 780, 810, 840, 870, 900, 940, 980, 1020, 1060, 1100, 1140, 1180, 1220, 1260, 1300, 1360, 1420, 1480, 1540, 1600, 1660, 1720, 1780, 1840, 1900};

            const uint Swiftcast = 167;         // 即刻咏唱
            const uint Lightspeed = 841;        // 光速
            const uint Dualcast = 1249;         // 连续咏唱 : 赤魔 : 被动 1级

            const uint LeyLines = 737;          // 黑魔纹       -15%
            const uint PresenceOfMind = 157;    // 神速咏唱     -20%
            // const uint Shifu = 7479;           // 士风
            // const uint BloodWeapon = 3625;     // 嗜血
            const uint Huton = 500;             // 风遁之术     -15%
            const uint HarmonyOfBody = 2715;    // 身体之座     -10%

            var me = Plugin.Player;

            if (me is null) return 0;

            var cast = 0;
            if (actionID > 0) {
                var info = this[actionID];
                if (info is null) return 0;
                if (me.IsCasting && me.CastActionId == info.ID) return (int)(me.TotalCastTime * 1000);
                cast = info.CastTime;
            } else {
                cast = 2500;
            }

            if (!adjusted) return cast;
            // return cast;

            // Spellspeed or Skillspeed
            var ss = cast > 0 ? Plugin.PlayerSpellSpeed : Plugin.PlayerSkillSpeed;

            var lv = me.Level;

            var speedBuff = 0;
            var haste = 0;

            // TODO: 黑魔: 雷云, 冰火技能等
            var astral_umbral = 100;

            Func<double> f = () => (1000 + 130 * (ss - SUB[lv]) / DIV[lv]);
            Func<double> gcd1 = () => (2000 - f()) * cast / 1000;
            Func<double> gcd2 = () => (100 - speedBuff) * (100 - haste) / 100;
            Func<double> gcd3 = () => gcd2() * gcd1() * astral_umbral / 100;

            // PluginLog.Debug($"SS: {ss}, sub: {SUB[lv]}, div: {DIV[lv]}, lv: {lv}, f: {f()}, gcd1: {gcd1()}, gcd2: {gcd2()}, gcd3: {gcd3()}, cast: {cast}");

            foreach (var status in me.StatusList) {
                // PluginLog.Debug($"statusID: {status.StatusId}");
                switch (status.StatusId)
                {
                    case Swiftcast:
                    case Lightspeed:
                    case Dualcast:
                        return 0;
                    case LeyLines:
                        speedBuff += 15; break;
                    case PresenceOfMind:
                        speedBuff += 20; break;
                    // case Shifu:
                    //     speedBuff += 13; break;
                    // case BloodWeapon:
                    //     speedBuff += 10; break;
                    case Huton:
                        speedBuff += 15; break;
                    case HarmonyOfBody:
                        speedBuff += 10; break;
                }
            }

            return (int)(gcd3() / 100);
        }

        public int RecastTime(uint actionID)
        {
            int recast = 0;
            unsafe {
                var am = ActionManager.Instance();
                if (am != null) {
                    var elapsed = am->GetRecastTimeElapsed(ActionType(actionID), actionID);
                    var total = am->GetRecastTime(ActionType(actionID), actionID);
                    recast = (int)((total - elapsed) * 1000);
                    // return ActionManager.GetAdjustedRecastTime(ActionType(actionID), actionID);
                }
            }
            return recast;
        }

        public int RecastGroup(uint actionID)
        {
            int group = 0;
            unsafe {
                var am = ActionManager.Instance();
                if (am != null) {
                    group = am->GetRecastGroup((int)ActionType(actionID), actionID);
                }
            }
            return group;
        }
    }
}