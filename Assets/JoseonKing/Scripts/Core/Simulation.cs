using System;
using System.Collections.Generic;
using System.Linq;

namespace JoseonKing.Core
{
    public enum DayPhase { Council, Memorial, Evening, Report, Ended }

    public enum EveningAction { Rest, EducateHeir, Banquet, Incognito }

    public enum Stance { Neutral, Support, Oppose }

    // 하루 마감 결과
    public class DayReport
    {
        public readonly List<string> Lines = new List<string>();
        public Dictionary<Stat, int> Before;
        public Dictionary<Stat, int> After;
        public bool Succession;
        public string NewKingName;
        public Ending Ending = Ending.None;
    }

    // 국정 시뮬레이션 본체. 화면과 LLM은 이 클래스만 부른다.
    public class Simulation
    {
        public readonly GameDatabase Db;
        public GameState S { get; private set; }
        public DayPhase Phase { get; private set; } = DayPhase.Council;
        public EventDef CurrentEvent { get; private set; }

        // 이 해가 끝나면 왕조 완주. 실제 조선의 마지막 해 1910 (설정에서 바꿀 수 있음)
        public int EndYear = 1910;

        // 마지막으로 내린 어명이 얼마나 제대로 시행됐는지 (왕권에 따라 다름)
        public float LastExecutionRate { get; private set; } = 1f;

        readonly Random rng;

        // 계승 순서. 역사가 바뀌어도 이름은 실제 순서를 빌려 쓴다.
        static readonly string[] KingNames = { "선조", "광해군", "인조", "효종", "현종", "숙종", "경종", "영조", "정조", "순조", "헌종", "철종" };

        public Simulation(GameDatabase db, int seed)
        {
            Db = db;
            rng = new Random(seed);
            S = new GameState();
            S.Record($"{S.KingName}가 보위에 오르다. 보령 {S.KingAge}세.");
        }

        // ───────── 조회 ─────────

        public List<MinisterDef> ActiveMinisters() =>
            Db.Ministers.Where(m => m.leaveYear <= 0 || S.Year < m.leaveYear).ToList();

        public bool IsPolicyReady(PolicyDef p) =>
            !S.PolicyReadyDay.TryGetValue(p.id, out var ready) || S.Day >= ready;

        public List<PolicyDef> AvailablePolicies() => Db.Policies.Where(IsPolicyReady).ToList();

        public bool ThreatKnown => S.Day <= threatKnownUntil;
        int threatKnownUntil = -1;

        public bool PeopleKnown { get; private set; }

        public Stance StanceOf(MinisterDef m, string policyId)
        {
            if (m.supports != null && Array.IndexOf(m.supports, policyId) >= 0) return Stance.Support;
            if (m.opposes != null && Array.IndexOf(m.opposes, policyId) >= 0) return Stance.Oppose;
            return Stance.Neutral;
        }

        // ───────── 1. 상참: 어명 ─────────

        // 왕권이 어명이 실제로 얼마나 시행되는지를 정한다. 숫자 대신 "지방에서 제대로 안 따른다"로 느껴진다.
        public float ExecutionRate()
        {
            int a = S.Get(Stat.Authority);
            return a < 20 ? 0.5f : a < 35 ? 0.7f : a < 50 ? 0.85f : 1f;
        }

        public string ApplyPolicy(string policyId)
        {
            if (Phase != DayPhase.Council) return "지금은 어명을 내릴 때가 아니다.";
            if (S.DecreedToday) return "오늘은 이미 큰 결정을 내렸다.";
            var p = Db.FindPolicy(policyId);
            if (p == null) return "그런 어명은 없다.";
            if (!IsPolicyReady(p)) return $"{p.name}은(는) 얼마 전에 시행했다. 아직 다시 내릴 수 없다.";

            float rate = ExecutionRate();
            LastExecutionRate = rate;
            ApplyDeltas(p.effects, rate);

            // 찬성한 대신의 당은 흡족하고, 반대한 대신의 당은 불만
            foreach (var m in ActiveMinisters())
            {
                int d = StanceOf(m, p.id) == Stance.Support ? 1 : StanceOf(m, p.id) == Stance.Oppose ? -1 : 0;
                if (d == 0) continue;
                if (m.faction == "east") S.Add(Stat.EastFavor, d);
                else if (m.faction == "west") S.Add(Stat.WestFavor, d);
            }

            if (p.revealsThreat) threatKnownUntil = S.Day + 3;

            int cd = p.cooldown > 0 ? p.cooldown : 2;
            S.PolicyReadyDay[p.id] = S.Day + cd + 1;
            S.DecreedToday = true;
            S.Record($"어명: {p.name}" + (rate < 1f ? " (지방에서 시행이 더딤)" : ""));
            return rate >= 1f ? $"{p.name}을(를) 명하였다." :
                   rate >= 0.8f ? $"{p.name}을(를) 명하였다. 다만 지방 수령들이 미적거린다는 말이 들린다." :
                   $"{p.name}을(를) 명하였으나, 조정과 지방이 왕명을 가볍게 여겨 제대로 시행될지 의문이다.";
        }

        // 대신이 올린 안건을 물리침. 올린 대신의 당이 조금 서운해한다.
        public void RejectProposal(string policyId, string proposerId)
        {
            var m = Db.FindMinister(proposerId);
            if (m == null) return;
            if (m.faction == "east") S.Add(Stat.EastFavor, -1);
            else if (m.faction == "west") S.Add(Stat.WestFavor, -1);
        }

        public void EndCouncil()
        {
            if (Phase != DayPhase.Council) return;
            PeopleKnown = false;
            CurrentEvent = DrawEvent();
            Phase = DayPhase.Memorial;
            if (CurrentEvent == null) Phase = DayPhase.Evening;
        }

        // ───────── 2. 상소·보고 ─────────

        EventDef DrawEvent()
        {
            var candidates = Db.Events
                .Where(Eligible)
                .OrderByDescending(e => e.priority)
                .ThenBy(_ => rng.Next())
                .ToList();

            foreach (var e in candidates)
            {
                float chance = e.chance <= 0f ? 1f : e.chance;
                if (rng.NextDouble() < chance) return e;
            }
            return null;
        }

        bool Eligible(EventDef e)
        {
            if (e.once && S.FiredOnce.Contains(e.id)) return false;
            if (e.minYear > 0 && S.Year < e.minYear) return false;
            if (e.maxYear > 0 && S.Year > e.maxYear) return false;
            if (!string.IsNullOrEmpty(e.season) && !SeasonMatches(e.season)) return false;
            if (e.requireFlags != null && e.requireFlags.Any(f => !S.Flags.Contains(f))) return false;
            if (e.forbidFlags != null && e.forbidFlags.Any(f => S.Flags.Contains(f))) return false;
            if (!string.IsNullOrEmpty(e.requireMinister) && ActiveMinisters().All(m => m.id != e.requireMinister)) return false;
            if (e.conditions != null && !e.conditions.All(Check)) return false;
            return true;
        }

        bool SeasonMatches(string season)
        {
            switch (season.ToLowerInvariant())
            {
                case "spring": return S.Season == Season.Spring;
                case "summer": return S.Season == Season.Summer;
                case "autumn": return S.Season == Season.Autumn;
                case "winter": return S.Season == Season.Winter;
            }
            return true;
        }

        public bool Check(StatCondition c)
        {
            int v = S.GetValue(c.stat);
            switch (c.op)
            {
                case ">=": return v >= c.value;
                case ">": return v > c.value;
                case "<=": return v <= c.value;
                case "<": return v < c.value;
                case "==": return v == c.value;
            }
            return true;
        }

        public string ChooseEventOption(int index)
        {
            if (Phase != DayPhase.Memorial || CurrentEvent == null) return "";
            var e = CurrentEvent;
            if (index < 0 || index >= e.choices.Length) return "";
            var c = e.choices[index];

            if (e.once) S.FiredOnce.Add(e.id);
            ApplyDeltas(c.effects);
            if (c.setFlags != null) foreach (var f in c.setFlags) S.Flags.Add(f);
            if (c.clearFlags != null) foreach (var f in c.clearFlags) S.Flags.Remove(f);
            RunAction(c.action);

            S.Record($"{e.title} — {c.text}");
            CurrentEvent = null;
            Phase = S.Ending != Ending.None ? DayPhase.Ended : DayPhase.Evening;
            return string.IsNullOrEmpty(c.result) ? c.text : c.result;
        }

        void RunAction(string action)
        {
            switch (action ?? "")
            {
                case "start_war":
                    S.AtWar = true;
                    S.Front = 20;
                    S.Enemy = 100;
                    S.Flags.Add("war");
                    S.Record("전쟁이 일어나다.");
                    break;
                case "flee":
                    S.Ending = Ending.Flight;
                    S.Record("임금이 도성을 버리고 몽진하다.");
                    break;
                case "end_war":
                    EndWar(true);
                    break;
                case "reveal_threat":
                    threatKnownUntil = S.Day + 3;
                    break;
                case "quiet_threat":
                    S.Add(Stat.Threat, -20);
                    break;
                case "enemy_loss":
                    S.Enemy = Math.Max(0, S.Enemy - 15);
                    break;
            }
        }

        // ───────── 3. 저녁 ─────────

        public bool CanDo(EveningAction a) => a != EveningAction.EducateHeir || S.HasHeir;

        public string DoEvening(EveningAction a)
        {
            if (Phase != DayPhase.Evening) return "";
            if (!CanDo(a)) return "아직 세자가 없다.";
            string msg = "";
            switch (a)
            {
                case EveningAction.Rest:
                    S.Add(Stat.Health, 3);
                    msg = "일찍 침전에 들었다.";
                    break;
                case EveningAction.EducateHeir:
                    S.Add(Stat.HeirEducation, 8);
                    S.Add(Stat.Health, -1);
                    msg = "세자를 불러 경서를 강론하였다.";
                    break;
                case EveningAction.Banquet:
                    S.Add(Stat.EastFavor, 3);
                    S.Add(Stat.WestFavor, 3);
                    S.Add(Stat.Treasury, -5);
                    msg = "대신들과 연회를 열었다.";
                    break;
                case EveningAction.Incognito:
                    S.Add(Stat.People, 2);
                    S.Add(Stat.Health, -2);
                    PeopleKnown = true;
                    msg = "평복 차림으로 몰래 민가를 돌아보았다. " + Chronicler.PeopleWords(S.Get(Stat.People));
                    break;
            }
            Phase = DayPhase.Report;
            return msg;
        }

        // ───────── 4. 마감 ─────────

        public DayReport EndDay()
        {
            var r = new DayReport { Before = Snapshot() };
            if (Phase != DayPhase.Report) { r.After = Snapshot(); return r; }

            SeasonalDrift(r);
            if (S.AtWar) WarStep(r);
            AdvanceTime(r);
            CheckEndings(r);

            r.After = Snapshot();
            r.Ending = S.Ending;
            S.DecreedToday = false;
            Phase = S.Ending != Ending.None ? DayPhase.Ended : DayPhase.Council;
            return r;
        }

        void SeasonalDrift(DayReport r)
        {
            int people = S.Get(Stat.People);
            int military = S.Get(Stat.Military);

            int income = 6 + people / 8;
            int upkeep = 2 + military / 8;
            S.Add(Stat.Treasury, income - upkeep);

            if (people > 50) S.Add(Stat.People, -1);
            else if (people < 50) S.Add(Stat.People, 1);
            if (S.Get(Stat.Treasury) < 0)
            {
                S.Add(Stat.People, -3);
                r.Lines.Add("곳간이 비어 관리들 녹봉이 밀린다. 민심이 흉흉하다.");
            }

            S.Add(Stat.Military, -1);

            if (S.Year >= 1586) S.Add(Stat.Threat, 2);
            else if (S.Year >= 1580) S.Add(Stat.Threat, 1);

            int east = S.Get(Stat.EastFavor), west = S.Get(Stat.WestFavor);
            if (east <= 15 || west <= 15)
            {
                S.Add(Stat.Authority, -3);
                r.Lines.Add(east <= 15 ? "동인들이 등을 돌렸다. 조정이 왕명을 따르지 않는다." : "서인들이 등을 돌렸다. 조정이 왕명을 따르지 않는다.");
            }
            if (Math.Abs(east - west) >= 40)
            {
                S.Add(Stat.Authority, -1);
                r.Lines.Add("한 당이 조정을 쥐고 흔든다.");
            }
        }

        void WarStep(DayReport r)
        {
            int enemy = S.Enemy, mil = S.Get(Stat.Military);
            int push = (enemy - mil) / 8;
            S.Front = Math.Max(0, Math.Min(100, S.Front + push));
            S.Enemy = Math.Max(0, enemy - (3 + mil / 5));
            S.Add(Stat.Military, -(2 + enemy / 10));
            S.Add(Stat.People, -3);
            S.Add(Stat.Treasury, -6);

            string place = Chronicler.FrontPlace(S.Front);
            r.Lines.Add(push > 0 ? $"적이 북상하여 {place}까지 이르렀다." :
                        push < 0 ? $"아군이 적을 밀어내 {place}까지 되찾았다." :
                        $"{place}에서 싸움이 교착되었다.");

            if (S.Enemy <= 0) EndWar(true, r);
        }

        void EndWar(bool won, DayReport r = null)
        {
            S.AtWar = false;
            S.Flags.Remove("war");
            S.Front = 0;
            S.Enemy = 0;
            S.Set(Stat.Threat, 15);
            if (won)
            {
                S.Add(Stat.People, 10);
                S.Add(Stat.Authority, 5);
                S.Record("적을 몰아내고 전쟁이 끝나다.");
                r?.Lines.Add("적이 물러갔다! 전쟁이 끝났다.");
            }
        }

        void AdvanceTime(DayReport r)
        {
            S.Day++;
            S.SeasonsThisReign++;
            S.TotalSeasonsReigned++;
            if (S.Season == Season.Winter)
            {
                S.Season = Season.Spring;
                S.Year++;
                S.KingAge++;
            }
            else S.Season = (Season)((int)S.Season + 1);

            int age = S.KingAge, loss = 0;
            if (age >= 30) loss += 1;
            if (age >= 40) loss += 1;
            if (age >= 50) loss += 2;
            if (age >= 60) loss += 3;
            if (loss > 0) S.Add(Stat.Health, -loss);
        }

        void CheckEndings(DayReport r)
        {
            if (S.Ending != Ending.None) return;

            if (S.AtWar && S.Front >= 100) { S.Ending = Ending.PalaceFallen; S.Record("적이 궁궐에 들이닥치다."); return; }
            if (S.Get(Stat.People) <= 0) { S.Ending = Ending.Revolt; S.Record("백성들이 들고일어나 도성이 불타다."); return; }
            if (S.Get(Stat.Authority) <= 0) { S.Ending = Ending.Deposed; S.Record("신하들이 반정을 일으켜 임금을 폐하다."); return; }

            if (S.Get(Stat.Treasury) < 0) S.NegativeTreasurySeasons++;
            else S.NegativeTreasurySeasons = 0;
            if (S.NegativeTreasurySeasons >= 3) { S.Ending = Ending.Bankrupt; S.Record("나라 살림이 무너지다."); return; }

            if (S.Year > EndYear) { S.Ending = Ending.Completed; S.Record("정해진 마지막 해까지 사직을 지켜내다."); return; }

            if (S.Get(Stat.Health) <= 0)
            {
                S.Record($"{S.KingName}가 승하하다. 재위 {S.SeasonsThisReign / 4}년.");
                if (!S.HasHeir) { S.Ending = Ending.NoHeir; return; }
                Succeed(r);
            }
        }

        void Succeed(DayReport r)
        {
            S.PastKings.Add($"{S.KingName} (재위 {S.SeasonsThisReign / 4}년)");
            int heir = S.Get(Stat.HeirEducation);
            S.Generation++;
            S.KingName = S.Generation - 1 < KingNames.Length ? KingNames[S.Generation - 1] : $"{S.Generation}대 임금";
            S.KingAge = 20;
            S.AccessionYear = S.Year;
            S.SeasonsThisReign = 0;
            S.Set(Stat.Health, 85);
            S.Set(Stat.Authority, 30 + heir * 4 / 10);
            S.Set(Stat.HeirEducation, 0);
            S.Flags.Remove("has_heir");
            S.Record($"세자가 즉위하니 {S.KingName}이다.");
            r.Succession = true;
            r.NewKingName = S.KingName;
            r.Lines.Add($"선왕이 승하하셨다. 세자가 보위에 올라 {S.KingName}이 되었다.");
        }

        // ───────── 도우미 ─────────

        // rate < 1 이면 이로운 효과만 줄어든다. 들어간 비용과 당파 반응은 그대로다.
        void ApplyDeltas(StatDelta[] deltas, float rate = 1f)
        {
            if (deltas == null) return;
            foreach (var d in deltas)
            {
                if (!StatInfo.TryParse(d.stat, out var s)) continue;
                int v = d.value;
                bool faction = s == Stat.EastFavor || s == Stat.WestFavor || s == Stat.Authority; // 왕권 회복까지 줄이면 악순환
                bool benefit = s == Stat.Threat ? v < 0 : v > 0;
                if (!faction && benefit && rate < 1f) v = (int)Math.Round(v * rate);
                S.Add(s, v);
            }
        }

        public Dictionary<Stat, int> Snapshot() => new Dictionary<Stat, int>(S.Stats);

        public static string EndingTitle(Ending e)
        {
            switch (e)
            {
                case Ending.Flight: return "몽진 — 도성을 버린 임금";
                case Ending.PalaceFallen: return "궁궐 함락";
                case Ending.Revolt: return "민란";
                case Ending.Deposed: return "반정 — 폐위된 임금";
                case Ending.Bankrupt: return "재정파탄 — 무너진 나라";
                case Ending.NoHeir: return "후사 없이 승하";
                case Ending.Completed: return "사직을 지켜내다";
            }
            return "";
        }

        public string EndingSummary()
        {
            int years = S.TotalSeasonsReigned / 4;
            var kings = new List<string>(S.PastKings) { $"{S.KingName} (재위 {S.SeasonsThisReign / 4}년)" };
            return $"{EndingTitle(S.Ending)}\n\n왕조 {S.Generation}대, 통산 {years}년.\n" + string.Join(" → ", kings);
        }
    }
}
