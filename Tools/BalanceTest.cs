// 밸런스 확인용: 유니티 없이 수백 판을 자동으로 돌려 엔딩 분포를 본다.
// 실행(Tools 폴더에서): mcs -r:System.Core.dll -out:test.exe ../Assets/JoseonKing/Scripts/Core/*.cs BalanceTest.cs && mono test.exe ../Assets/StreamingAssets/JoseonKing

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.Globalization;
using JoseonKing.Core;

static class HeadlessTest
{
    // 테스트용 작은 JSON 파서 (유니티에서는 JsonUtility가 이 일을 함)
    class J { string s; int i; public J(string t) { s = t; }
        void W() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        public object V() { W(); char c = s[i];
            if (c == '{') { i++; var d = new Dictionary<string, object>(); W(); if (s[i] == '}') { i++; return d; }
                while (true) { W(); string k = (string)V(); W(); i++; d[k] = V(); W(); if (s[i++] == '}') return d; } }
            if (c == '[') { i++; var l = new List<object>(); W(); if (s[i] == ']') { i++; return l; }
                while (true) { l.Add(V()); W(); if (s[i++] == ']') return l; } }
            if (c == '"') { i++; var sb = new StringBuilder(); while (s[i] != '"') { if (s[i] == '\\') { i++; sb.Append(s[i] == 'n' ? '\n' : s[i]); } else sb.Append(s[i]); i++; } i++; return sb.ToString(); }
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int st = i; while (i < s.Length && "-+.0123456789eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture); } }

    static object Map(object v, Type t)
    {
        if (v == null) return null;
        if (t == typeof(string)) return (string)v;
        if (t == typeof(int)) return (int)(double)v;
        if (t == typeof(float)) return (float)(double)v;
        if (t == typeof(bool)) return (bool)v;
        if (t.IsArray) { var l = (List<object>)v; var a = Array.CreateInstance(t.GetElementType(), l.Count); for (int k = 0; k < l.Count; k++) a.SetValue(Map(l[k], t.GetElementType()), k); return a; }
        var o = Activator.CreateInstance(t); var d = (Dictionary<string, object>)v;
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) if (d.ContainsKey(f.Name)) f.SetValue(o, Map(d[f.Name], f.FieldType));
        return o;
    }

    static T Load<T>(string path) { return (T)Map(new J(File.ReadAllText(path)).V(), typeof(T)); }

    static GameDatabase Db(string dir)
    {
        var db = new GameDatabase();
        db.Policies.AddRange(Load<PolicyList>(Path.Combine(dir, "policies.json")).items);
        db.Ministers.AddRange(Load<MinisterList>(Path.Combine(dir, "ministers.json")).items);
        db.Events.AddRange(Load<EventList>(Path.Combine(dir, "events.json")).items);
        db.Dialogue.AddRange(Load<DialogueList>(Path.Combine(dir, "dialogue.json")).items);
        return db;
    }

    // bot: 0 = 무작위, 1 = 대비형(군사·성곽·진휼 위주, 휴식/교육)
    static Simulation Play(GameDatabase db, int seed, int bot, int maxDays = 1500)
    {
        var sim = new Simulation(db, seed);
        var rng = new Random(seed * 7 + 1);
        for (int d = 0; d < maxDays && sim.Phase != DayPhase.Ended; d++)
        {
            var avail = sim.AvailablePolicies();
            PolicyDef pick = null;
            if (bot == 0) { if (rng.NextDouble() < 0.7 && avail.Count > 0) pick = avail[rng.Next(avail.Count)]; }
            else pick = SmartPick(sim, avail);
            if (pick != null)
            {
                sim.ApplyPolicy(pick.id);
            }
            sim.EndCouncil();
            if (sim.Phase == DayPhase.Memorial)
            {
                var e = sim.CurrentEvent;
                int idx = bot == 0 ? rng.Next(e.choices.Length) : SmartChoice(e);
                sim.ChooseEventOption(idx);
            }
            if (sim.Phase == DayPhase.Ended) break;
            EveningAction ev = bot == 0 ? (EveningAction)rng.Next(4)
                : sim.S.Get(Stat.Health) < 50 ? EveningAction.Rest
                : sim.S.HasHeir ? EveningAction.EducateHeir : EveningAction.Rest;
            if (!sim.CanDo(ev)) ev = EveningAction.Rest;
            sim.DoEvening(ev);
            sim.EndDay();
        }
        return sim;
    }

    static PolicyDef SmartPick(Simulation sim, List<PolicyDef> avail)
    {
        var s = sim.S;
        Func<string, PolicyDef> P = id => avail.FirstOrDefault(p => p.id == id);
        if (s.Get(Stat.People) < 35 && P("relief") != null && s.Get(Stat.Treasury) > 35) return P("relief");
        if (s.Get(Stat.Treasury) < 10 && P("tax_raise") != null && s.Get(Stat.People) > 30) return P("tax_raise");
        if (Math.Min(s.Get(Stat.EastFavor), s.Get(Stat.WestFavor)) < 30 && s.Get(Stat.Authority) > 40 && P("balance") != null) return P("balance");
        if (s.Get(Stat.Authority) < 30 && P("suppress_censors") != null) return P("suppress_censors");
        if ((s.AtWar || s.Year >= 1585) && s.Get(Stat.Treasury) > 40 && P("raise_army") != null) return P("raise_army");
        if (s.Year >= 1585 && s.Get(Stat.Treasury) > 35 && P("fortify") != null) return P("fortify");
        if (s.Get(Stat.People) < 60 && s.Get(Stat.Treasury) > 40 && P("tribute_reform") != null) return P("tribute_reform");
        return null;
    }

    static int SmartChoice(EventDef e)
    {
        for (int i = 0; i < e.choices.Length; i++) if (e.choices[i].action == "flee") continue; else if (i == 0) return 0;
        return 0;
    }

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var db = Db(args[0]);
        var errs = db.Validate();
        Console.WriteLine($"데이터: 어명 {db.Policies.Count}, 대신 {db.Ministers.Count}, 사건 {db.Events.Count}, 오류 {errs.Count}");
        foreach (var e in errs) Console.WriteLine("  오류: " + e);

        // 대본 회의 하루 뽑아 보기 (전쟁 중·곳간 빈 상황도)
        foreach (var setup in new[] { "평시", "전쟁+빈곳간" })
        {
            var sim = new Simulation(db, 7);
            if (setup != "평시") { sim.S.AtWar = true; sim.S.Front = 55; sim.S.Enemy = 80; sim.S.Flags.Add("war"); sim.S.Set(Stat.Treasury, 5); sim.S.Set(Stat.Threat, 60); sim.S.Set(Stat.Authority, 25); }
            var rng = new Random(3);
            var council = new ScriptedCouncil(sim, rng);
            Console.WriteLine($"\n=== {setup}: {sim.S.DateText} ===");
            foreach (var r in Chronicler.MorningReport(sim, rng)) Console.WriteLine($"  [{r.Speaker}] {r.Text}");
            council.BeginDay();
            while (council.Current != null)
            {
                var item = council.Current;
                var pr = council.ProposeLine(item);
                Console.WriteLine($"  ▶ 안건 {db.FindPolicy(item.PolicyId).name} — {db.FindMinister(pr.MinisterId).name}: {pr.Text}");
                foreach (var l in council.AskAll(item)) Console.WriteLine($"    {db.FindMinister(l.MinisterId).name}[{l.Stance}]: {l.Text}");
                bool ok = sim.ApplyPolicy(item.PolicyId).Length > 0;
                foreach (var l in council.React(item, true)) Console.WriteLine($"    → {db.FindMinister(l.MinisterId).name}: {l.Text}");
                council.Next();
            }
        }

        foreach (int bot in new[] { 0, 1 })
        {
            var endings = new Dictionary<string, int>();
            int totalDays = 0, gens = 0, wars = 0, won = 0; var endYear = new Dictionary<string, List<int>>();
            const int N = 300;
            for (int i = 0; i < N; i++)
            {
                var sim = Play(db, i + 1, bot);
                string k = sim.S.Ending == Ending.None ? "진행중" : sim.S.Ending.ToString();
                endings[k] = endings.TryGetValue(k, out var c) ? c + 1 : 1;
                totalDays += sim.S.TotalSeasonsReigned;
                gens += sim.S.Generation;
                if (sim.S.Chronicle.Any(l => l.Contains("전쟁이 일어나다"))) wars++;
                if (sim.S.Chronicle.Any(l => l.Contains("전쟁이 끝나다"))) won++;
                if (!endYear.ContainsKey(k)) endYear[k] = new List<int>(); endYear[k].Add(sim.S.Year);
            }
            Console.WriteLine($"\n[{(bot == 0 ? "무작위 플레이" : "대비형 플레이")}] {N}판, 평균 {totalDays / (float)N / 4:F1}년, 평균 {gens / (float)N:F2}대, 전쟁 발생 {wars}판, 승전 {won}판, 왕 1명 평균 재위 {totalDays / 4f / gens:F1}년");
            foreach (var kv in endings.OrderByDescending(x => x.Value)) Console.WriteLine($"  {kv.Key}: {kv.Value}  (평균 {endYear[kv.Key].Average():F0}년에 끝)");
        }

        var sample = Play(db, 42, 1);
        Console.WriteLine("\n[대비형 한 판 실록 끝부분]");
        foreach (var l in sample.S.Chronicle.Skip(Math.Max(0, sample.S.Chronicle.Count - 25))) Console.WriteLine("  " + l);
        Console.WriteLine("\n" + Chronicler.Epilogue(sample));
    }
}
