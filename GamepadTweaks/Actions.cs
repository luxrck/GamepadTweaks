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

        public bool Targeted => TargetID != Configuration.DefaultInvalidGameObjectID;
        public bool IsValid => this.ID > 0;
        public bool IsCasting => Plugin.Player is not null && Plugin.Player.IsCasting && Plugin.Actions.Equals(Plugin.Player.CastActionId, ID);

        // 由于即刻咏唱的存在, CastTime必须动态获取
        // TODO: Should get Action cast detail info even not in casting.
        public uint CastTimeTotalMilliseconds => Plugin.Actions.Cooldown(ID, adjusted: false);
        public uint AdjustedCastTimeTotalMilliseconds => Plugin.Actions.Cooldown(ID, adjusted: true);

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
                    var current = (int)(me.CurrentCastTime * 1000);
                    var total = (int)(me.TotalCastTime * 1000);
                    var delay = 100;
                    // 滑步!!!
                    while (current < total - Configuration.GlobalCoolingDown.SlidingWindow) {
                        await Task.Delay(delay);
                        if (!me.IsCasting) {
                            return false;
                        }
                        current += delay;
                    }
                    // PluginLog.Debug($"[Casting] action: {Plugin.Actions.Name(ID)}, wait: {total - Configuration.GlobalCoolingDown.SlidingWindow}, total: {total}");
                    Finished = true;
                    return true;
                } else {
                    // why reach here ???
                    // abbbb[bc]. bc成功,且都需要咏唱.
                    // UseAction[b], b is casting -> Put(b) -> UseAction[c], c is casting -> UpdateState(b) -> Put(c) -> UpdteState(c)
                    return false;
                }
            } else {
                // if (CastTimeTotalMilliseconds > 0) {
                //     // 应该咏唱但是确没有, 被打断了?
                //     // if ((DateTime.Now - LastTime).TotalMilliseconds < CastTimeTotalMilliseconds) return false;
                //     // 应该咏唱但是超出了时间, 被阻塞太久了?
                //     // if ((DateTime.Now - LastTime).TotalMilliseconds >= CastTimeTotalMilliseconds) return false;
                //     return false;
                // } else {
                //     return true;
                // }
                return false;
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
        public bool CanTargetHostil;
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
        private Dictionary<uint, uint[]> AliasInfo = new Dictionary<uint, uint[]>() {
            // AST
            { 17055, new uint[] {4401, 4402, 4403, 4404, 4405, 4406} },   // 出卡
            { 25869, new uint[] {7444, 7445} }, // 出王冠卡

            // SMN
            { 25822, new uint[] {3582} },    // 星极超流
            { 3579,  new uint[] {25820} },   // 毁荡
            { 16511, new uint[] {25826} },   // 迸裂
            { 25883, new uint[] {25825, 25824, 25823, 25819, 25818, 25817, 25813, 25812, 25811, 25810, 25809, 25808} },    // 宝石耀
            { 25884, new uint[] {25829, 25828, 25827, 25816, 25815, 25814} },    // 宝石辉
            { 25800, new uint[] {3581, 7427} },    //以太蓄能
            { 25804, new uint[] {25807} },   // 绿
            { 25803, new uint[] {25806} },   // 黄
            { 25802, new uint[] {25805} },   // 红
        };

        private Dictionary<uint, uint> AliasMap = new Dictionary<uint, uint>();

        // private Dictionary<uint, GameAction> ActionsMap = new Dictionary<uint, GameAction>();
        private Dictionary<uint, ActionInfo> ActionsInfoMap = new Dictionary<uint, ActionInfo>();
        private Dictionary<string, uint> ActionsNameMap = new Dictionary<string, uint>();

        public Actions()
        {
            foreach (var a in AliasInfo) {
                foreach (var b in a.Value) {
                    AliasMap[b] = a.Key;
                }
                AliasMap[a.Key] = a.Key;
            }

            // foreach (var (lang, name, id) in ActionsInfo) {
            //     if (!ActionsMap.ContainsKey(id))
            //         ActionsMap[id] = new GameAction() {
            //             ID = id,
            //         };
            //     ActionsMap[id].Names[lang] = name;
            // }

            try {
                var content = File.ReadAllText(Configuration.ActionFile.ToString());
                var infos = JsonConvert.DeserializeObject<List<ActionInfo>>(content) ?? new List<ActionInfo>();
                foreach (var info in infos) {
                    if (!ActionsInfoMap.ContainsKey(info.ID)) {
                        ActionsInfoMap[info.ID] = info;
                    }
                    if (ActionsNameMap.ContainsKey(info.Name)) {
                        PluginLog.Warning($"Duplicate actions: {info.Name}. {ActionsNameMap[info.Name]} already exists while incoming {info.ID}");
                    } else {
                        ActionsNameMap[info.Name] = info.ID;
                    }
                }
            } catch(Exception e) {
                PluginLog.Error($"Exception: {e}");
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

        public bool Contains(string action) => ActionsNameMap.ContainsKey(action);
        public bool Equals(uint a1, uint a2) => BaseActionID(a1) == BaseActionID(a2);

        public uint this[string a] => Contains(a) ? ActionsNameMap[a] : 0;
        public ActionInfo? this[uint i] => ActionsInfoMap.ContainsKey(i) ? ActionsInfoMap[i] : null;
        public uint ID(string a) => this[a];
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

        // // Broken
        // public float CastTime(uint actionID)
        // {
        //     float cast = 0f;
        //     unsafe {
        //         var am = (ActionManagerFields*)ActionManager.Instance();
        //         if (am != null) {
        //             // cast = am->GetAdjustedCastTime(ActionType(actionID), actionID);
        //             if (am->IsCasting && am->CurrentGCDAction == actionID) {
        //                 cast = am->CastTime;
        //             }
        //         }
        //     }
        //     return cast;
        // }

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
        public uint Cooldown(uint actionID, bool adjusted = true)
        {
            var SUB = new uint[] {55, 56, 57, 60, 62, 65, 68, 70, 73, 76, 78, 82, 85, 89, 93, 96, 100, 104, 109, 113, 116, 122, 127, 133, 138, 144, 150, 155, 162, 168, 173, 181, 188, 194, 202, 209, 215, 223, 229, 236, 244, 253, 263, 272, 283, 292, 302, 311, 322, 331, 341, 342, 344, 345, 346, 347, 349, 350, 351, 352, 354, 355, 356, 357, 358, 359, 360, 361, 362, 363, 364, 365, 366, 367, 368, 370, 372, 374, 376, 378, 380, 382, 384, 386, 388, 390, 392, 394, 396, 398, 400};
            var DIV = new uint[] {55, 56, 57, 60, 62, 65, 68, 70, 73, 76, 78, 82, 85, 89, 93, 96, 100, 104, 109, 113, 116, 122, 127, 133, 138, 144, 150, 155, 162, 168, 173, 181, 188, 194, 202, 209, 215, 223, 229, 236, 244, 253, 263, 272, 283, 292, 302, 311, 322, 331, 341, 366, 392, 418, 444, 470, 496, 522, 548, 574, 600, 630, 660, 690, 720, 750, 780, 810, 840, 870, 900, 940, 980, 1020, 1060, 1100, 1140, 1180, 1220, 1260, 1300, 1360, 1420, 1480, 1540, 1600, 1660, 1720, 1780, 1840, 1900};

            const uint Swiftcast = 167;         // 即刻咏唱
            const uint Lightspeed = 841;        // 光速

            const uint LeyLines = 737;          // 黑魔纹
            const uint PresenceOfMind = 157;    // 神速咏唱
            // const uint Shifu = 7479;           // 士风
            // const uint BloodWeapon = 3625;     // 嗜血
            const uint Huton = 500;             // 风遁之术

            var me = Plugin.Player;

            var info = this[actionID];

            if (me is null || info is null) return (uint)(info?.CastTime ?? 0);
            if (me.IsCasting && me.CastActionId == info.ID) return (uint)(me.TotalCastTime * 1000);

            var cast = info.CastTime;

            if (!adjusted) return (uint)cast;

            // Spellspeed or Skillspeed
            var ss = cast > 0 ? Plugin.PlayerSpellSpeed : Plugin.PlayerSkillSpeed;

            var lv = me.Level;
            var speedBuff = 0;
            var haste = 0;
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
                }
            }

            return (uint)(gcd3() / 100);
        }

        public float RecastTimeRemain(uint actionID)
        {
            float recast = 0f;
            unsafe {
                var am = ActionManager.Instance();
                if (am != null) {
                    var elapsed = am->GetRecastTimeElapsed(ActionType(actionID), actionID);
                    var total = am->GetRecastTime(ActionType(actionID), actionID);
                    // var atotal = am->GetAdjustedRecastTime(ActionType(actionID), actionID);
                    // var acast = am->GetAdjustedCastTime(ActionType(actionID), actionID,1,1);
                    recast = total - elapsed;
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