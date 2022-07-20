using Dalamud;
using Dalamud.Game.ClientState;
using Dalamud.Logging;

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
        public bool Finished = true;

        public string Name => Names.ContainsKey(Plugin.ClientLanguage) ? Names[Plugin.ClientLanguage] : String.Empty;
        public Dictionary<string, string> Names = new Dictionary<string, string>();

        // public DateTime DelayTo = DateTime.Now;

        public bool Targeted => TargetID != Configuration.DefaultInvalidGameObjectID;
        public bool IsValid => this.ID > 0;
        public bool IsCasting => Plugin.Player is not null && Plugin.Player.IsCasting && Plugin.Actions.Equals(Plugin.Player.CastActionId, ID);

        // 由于即刻咏唱的存在, CastTime必须动态获取
        // TODO: Should get Action cast detail info even not in casting.
        public uint CastTimeTotalMilliseconds
        {
            get {
                var me = Plugin.Player;
                if (me is not null && me.IsCasting && me.CastActionId == ID) {
                    var cast = 0f;
                    cast = me.TotalCastTime;
                    if (cast <= 0.1) {
                        return 0;
                    }
                    return (uint)(cast * 1000);
                }
                return 0;
            }
        }

        public async Task<bool> Wait()
        {
            if (!Plugin.Ready || Plugin.Player is null) return false;
            if (Finished || Status != ActionStatus.Ready) return true;

            // 有些技能, 咏唱时间大于gcd(例如红宝石灾祸). 如果卡着咏唱结束时间连点的话,
            // 游戏会连续释放2次UseAction[status==Ready] ???
            // UseAction[a,Ready,UseType==0] : me.IsCasting == false -> UseAction[a,Ready,UseType==1] : me.IsCasting == true
            // 由于第一次UseAction时, IsCasting == false. 因此Finished == true && status == Ready, 函数会立即返回true.
            // TODO: GetAdjustedCastTime 有问题?
            // if (CastTimeTotalMilliseconds == 0) return true;

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

        private List<(string Lang, string Name, uint ID)> ActionsInfo = new List<(string, string, uint)> {
            // SGE
            {("zh", "均衡诊断", 24291)},
            {("zh", "白牛清汁", 24303)},
            {("zh", "灵橡清汁", 24296)},
            {("zh", "混合", 24317)},
            {("zh", "输血", 24305)},
            {("en", "Diagnosis", 24284)},
            {("en", "Eukrasian Diagnosis", 24291)},
            {("en", "Taurochole", 24303)},
            {("en", "Druochole", 24296)},
            {("en", "Krasis", 24317)},
            {("en", "Haima", 24305)},

            // WHM
            {("zh", "再生", 137)},
            {("zh", "天赐祝福", 140)},
            {("zh", "神祝祷", 7432)},
            {("zh", "神名", 3570)},
            {("zh", "水流幕", 25861)},
            {("zh", "安慰之心", 16531)},
            {("zh", "庇护所", 3569)},
            {("zh", "礼仪之铃", 25862)},
            {("en", "Regen", 137)},
            {("en", "Benediction", 140)},
            {("en", "Divine Benison", 7432)},
            {("en", "Tetragrammaton", 3570)},
            {("en", "Aquaveil", 25861)},
            {("en", "Afflatus Solace", 16531)},
            {("en", "Asylum", 3569)},
            {("en", "Liturgy of the Bell", 25862)},

            // AST
            {("zh", "先天禀赋", 3614)},
            {("zh", "出卡", 17055)},
            {("zh", "吉星相位", 3595)},
            {("zh", "星位合图", 3612)},
            {("zh", "出王冠卡", 25869)},
            {("zh", "天星交错", 16556)},
            {("zh", "擢升", 25873)},
            {("zh", "地星", 7439)},
            {("zh", "小奥秘卡", 7443)},
            {("zh", "抽卡", 3590)},
            {("en", "Essential Dignity", 3614)},
            {("en", "Play", 17055)},
            {("en", "Aspected Benefic", 3595)},
            {("en", "Synastry", 3612)},
            {("en", "Crown Play", 25869)},
            {("en", "Celestial Intersection", 16556)},
            {("en", "Exaltation", 25873)},

            // SCH
            {("zh", "鼓舞激励之策", 185)},
            {("zh", "野战治疗阵", 188)},
            {("zh", "生命活性法", 189)},
            {("zh", "深谋远虑之策", 7434)},
            {("zh", "以太契约", 7437)},
            {("zh", "生命回生法", 25867)},
            {("en", "Adloquium", 185)},
            {("en", "Lustrate", 189)},
            {("en", "Excogitation", 7434)},
            {("en", "Aetherpact", 7423)},
            {("en", "Protraction", 25867)},

            // WAR
            {("zh", "重劈", 31)},
            {("zh", "凶残裂", 37)},
            {("zh", "超压斧", 41)},
            {("zh", "秘银暴风", 16462)},
            {("zh", "暴风斩", 42)},
            {("zh", "暴风碎", 45)},
            {("zh", "飞斧", 46)},
            {("zh", "守护", 48)},
            {("zh", "铁壁", 7531)},
            {("zh", "雪仇", 7535)},
            {("zh", "下踢", 7540)},
            {("zh", "蛮荒崩裂", 25753)},
            {("zh", "原初的解放", 7389)},

            // SMN
            {("zh", "龙神附体", 3581)},
            {("zh", "龙神召唤", 7427)},
            {("zh", "以太蓄能", 25800)},
            {("zh", "死星核爆", 3582)},
            {("zh", "星极脉冲", 25820)},
            {("zh", "星极超流", 25822)},
            {("zh", "宝石耀", 25883)},
            {("zh", "宝石辉", 25884)},
            {("zh", "风神召唤", 25807)},
            {("zh", "土神召唤", 25806)},
            {("zh", "火神召唤", 25805)},
            {("zh", "绿宝石召唤", 25804)},
            {("zh", "黄宝石召唤", 25803)},
            {("zh", "红宝石召唤", 25802)},
            {("zh", "毁荡", 3579)},
            {("zh", "能量吸收", 16508)},
            {("zh", "能量抽取", 16510)},
            {("zh", "迸裂", 16511)},
            {("zh", "三重灾祸", 25826)},
            {("zh", "龙神迸发", 7429)},
            {("zh", "溃烂爆发", 181)},
            {("zh", "痛苦核爆", 3578)},
            {("zh", "绿宝石灾祸", 25829)},
            {("zh", "黄宝石灾祸", 25828)},
            {("zh", "红宝石灾祸", 25827)},
            {("zh", "绿宝石迸裂", 25816)},
            {("zh", "黄宝石迸裂", 25815)},
            {("zh", "红宝石迸裂", 25814)},
            {("zh", "绿宝石之仪", 25825)},
            {("zh", "黄宝石之仪", 25824)},
            {("zh", "红宝石之仪", 25823)},
            {("zh", "绿宝石毁荡", 25819)},
            {("zh", "黄宝石毁荡", 25818)},
            {("zh", "红宝石毁荡", 25817)},
            {("zh", "绿宝石毁坏", 25813)},
            {("zh", "黄宝石毁坏", 25812)},
            {("zh", "红宝石毁坏", 25811)},
            {("zh", "绿宝石毁灭", 25810)},
            {("zh", "黄宝石毁灭", 25809)},
            {("zh", "红宝石毁灭", 25808)},
        };

        private Dictionary<uint, GameAction> ActionsMap = new Dictionary<uint, GameAction>();

        public Actions()
        {
            foreach (var a in AliasInfo) {
                foreach (var b in a.Value) {
                    AliasMap[b] = a.Key;
                }
                AliasMap[a.Key] = a.Key;
            }

            foreach (var (lang, name, id) in ActionsInfo) {
                if (!ActionsMap.ContainsKey(id))
                    ActionsMap[id] = new GameAction() {
                        ID = id,
                    };
                ActionsMap[id].Names[lang] = name;
            }
        }

        public bool Contains(string action) => ActionsInfo.Any(x => x.Name == action);
        public bool Equals(uint a1, uint a2) => BaseActionID(a1) == BaseActionID(a2);

        public uint this[string a] => Contains(a) ? ActionsInfo.First(x => x.Name == a).ID : 0;
        public string this[uint i] => ActionsMap.ContainsKey(i) ? ActionsMap[i].Name : String.Empty;
        public uint ID(string a) => this[a];
        public string Name(uint i) => this[i];

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