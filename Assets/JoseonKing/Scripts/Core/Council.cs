// 대신 회의(상참) — 전부 대본으로 돈다. 서버도 LLM도 쓰지 않는다.
// 흐름: 대신이 안건을 올림 → 임금이 의견을 물음(전원 또는 한 명) → 윤허/불허 → 대신 반응.
// 임금이 직접 하교(명령 목록에서 고름)할 수도 있다.

using System;
using System.Collections.Generic;
using System.Linq;

namespace JoseonKing.Core
{
    public class CouncilLine
    {
        public string MinisterId;   // null이면 지문
        public string Text;
        public Stance Stance;
    }

    public class AgendaItem
    {
        public string PolicyId;
        public string ProposerId;   // 임금이 직접 올리면 null
        public bool RaisedByKing => ProposerId == null;
        public readonly HashSet<string> Heard = new HashSet<string>();
        public bool Decided;
    }

    public class ScriptedCouncil
    {
        readonly Simulation sim;
        readonly Random rng;
        readonly Dictionary<string, int> lastUsedDay = new Dictionary<string, int>();

        public List<AgendaItem> Agenda { get; private set; } = new List<AgendaItem>();
        int index;

        public AgendaItem Current => index < Agenda.Count ? Agenda[index] : null;

        public ScriptedCouncil(Simulation sim, Random rng)
        {
            this.sim = sim;
            this.rng = rng;
        }

        // ───────── 안건 ─────────

        // 그날 형편에 맞는 안건을 대신들이 올린다. 급한 안건 최대 2개, 없으면 평시 안건 1개.
        public void BeginDay()
        {
            Agenda = new List<AgendaItem>();
            index = 0;
            var ready = sim.AvailablePolicies();

            var urgent = ready.Where(p => p.agenda != null && p.agenda.Length > 0 && p.agenda.All(sim.Check))
                              .OrderBy(_ => rng.Next()).ToList();
            var normal = ready.Where(p => p.agenda == null || p.agenda.Length == 0)
                              .OrderBy(_ => rng.Next()).ToList();

            foreach (var p in urgent.Take(2)) Agenda.Add(new AgendaItem { PolicyId = p.id, ProposerId = PickProposer(p) });
            if (Agenda.Count == 0 && normal.Count > 0)
                Agenda.Add(new AgendaItem { PolicyId = normal[0].id, ProposerId = PickProposer(normal[0]) });

            Agenda.RemoveAll(a => a.ProposerId == null); // 올릴 대신이 없으면 안건도 없다
        }

        string PickProposer(PolicyDef p)
        {
            var ministers = sim.ActiveMinisters();
            var supporters = ministers.Where(m => sim.StanceOf(m, p.id) == Stance.Support).ToList();
            if (supporters.Count > 0) return supporters[rng.Next(supporters.Count)].id;
            var neutral = ministers.Where(m => sim.StanceOf(m, p.id) == Stance.Neutral).ToList();
            if (neutral.Count > 0) return neutral[rng.Next(neutral.Count)].id;
            return null;
        }

        // 임금이 직접 하교 — 지금 안건 자리에 끼워 넣는다
        public AgendaItem RaiseByKing(string policyId)
        {
            var item = new AgendaItem { PolicyId = policyId, ProposerId = null };
            Agenda.Insert(index, item);
            return item;
        }

        public void Next() => index++;

        // ───────── 대사 ─────────

        public CouncilLine ProposeLine(AgendaItem item)
        {
            var text = Pick("propose", item.ProposerId, item.PolicyId, null);
            return new CouncilLine { MinisterId = item.ProposerId, Text = text, Stance = Stance.Support };
        }

        // 아직 말하지 않은 대신 모두에게 의견을 묻는다 (안건 올린 대신은 이미 말함)
        public List<CouncilLine> AskAll(AgendaItem item)
        {
            var lines = new List<CouncilLine>();
            foreach (var m in sim.ActiveMinisters())
            {
                if (m.id == item.ProposerId || item.Heard.Contains(m.id)) continue;
                lines.Add(AskOne(item, m.id));
            }
            return lines;
        }

        public CouncilLine AskOne(AgendaItem item, string ministerId)
        {
            var m = sim.Db.FindMinister(ministerId);
            if (m == null) return null;
            item.Heard.Add(ministerId);
            if (ministerId == item.ProposerId)
                return new CouncilLine { MinisterId = ministerId, Stance = Stance.Support, Text = Pick("opinion", ministerId, item.PolicyId, null) };
            var st = sim.StanceOf(m, item.PolicyId);
            string text = Pick("opinion", ministerId, item.PolicyId, null);
            // 왕권이 약하면 신하들 말에 대놓고 가시가 돋는다
            if (Contexts().Contains("weak") && rng.Next(2) == 0) text += " " + Pick("weak_tail", ministerId, item.PolicyId, null);
            return new CouncilLine { MinisterId = ministerId, Stance = st, Text = text };
        }

        // 윤허/불허 뒤 반응: 가장 기뻐하거나 가장 언짢아할 대신 한두 명
        public List<CouncilLine> React(AgendaItem item, bool approved)
        {
            var lines = new List<CouncilLine>();
            var ministers = sim.ActiveMinisters();
            var sup = ministers.Where(m => sim.StanceOf(m, item.PolicyId) == Stance.Support || m.id == item.ProposerId)
                               .OrderBy(_ => rng.Next()).FirstOrDefault();
            var opp = ministers.Where(m => sim.StanceOf(m, item.PolicyId) == Stance.Oppose && m.id != item.ProposerId)
                               .OrderBy(_ => rng.Next()).FirstOrDefault();
            string kind = approved ? "approved" : "rejected";
            if (sup != null) lines.Add(new CouncilLine { MinisterId = sup.id, Stance = Stance.Support, Text = Pick(kind, sup.id, item.PolicyId, "support") });
            if (opp != null) lines.Add(new CouncilLine { MinisterId = opp.id, Stance = Stance.Oppose, Text = Pick(kind, opp.id, item.PolicyId, "oppose") });
            return lines;
        }

        // 지금 형편 (대사 고를 때 씀)
        public List<string> Contexts()
        {
            var c = new List<string>();
            if (sim.S.AtWar) c.Add("war");
            if (sim.S.Get(Stat.Treasury) < 15) c.Add("poor");
            if (sim.S.Get(Stat.People) < 30) c.Add("unrest");
            if (sim.S.Get(Stat.Authority) < 30) c.Add("weak");
            return c;
        }

        // (종류, 대신, 어명, 찬반, 정황)이 가장 구체적으로 맞는 대사를 고른다. 같은 대사가 연달아 나오지 않게 한다.
        string Pick(string kind, string ministerId, string policyId, string stance)
        {
            var ctx = Contexts();
            DialogueLine best = null;
            int bestScore = -1;
            var ties = new List<DialogueLine>();

            foreach (var d in sim.Db.Dialogue)
            {
                if (d.kind != kind) continue;
                if (d.minister != "*" && d.minister != ministerId) continue;
                if (d.policy != "*" && d.policy != policyId) continue;
                if (!string.IsNullOrEmpty(d.stance) && d.stance != stance) continue;
                if (!string.IsNullOrEmpty(d.context) && !ctx.Contains(d.context)) continue;

                int score = (d.minister != "*" ? 4 : 0) + (d.policy != "*" ? 2 : 0) +
                            (!string.IsNullOrEmpty(d.context) ? 3 : 0) + (!string.IsNullOrEmpty(d.stance) ? 1 : 0);
                if (score > bestScore) { bestScore = score; ties.Clear(); }
                if (score == bestScore) ties.Add(d);
            }
            if (ties.Count == 0) return "…….";

            // 최근에 쓴 대사는 피한다
            var fresh = ties.Where(t => !lastUsedDay.TryGetValue(t.text, out var day) || sim.S.Day - day > 6).ToList();
            best = (fresh.Count > 0 ? fresh : ties)[rng.Next(fresh.Count > 0 ? fresh.Count : ties.Count)];
            lastUsedDay[best.text] = sim.S.Day;
            return Fill(best.text, ministerId, policyId);
        }

        string Fill(string text, string ministerId, string policyId)
        {
            var m = sim.Db.FindMinister(ministerId);
            var p = sim.Db.FindPolicy(policyId);
            return text.Replace("{name}", m?.name ?? "")
                       .Replace("{policy}", p?.name ?? "")
                       .Replace("{rice}", Chronicler.Rice(sim.S.Get(Stat.Treasury)))
                       .Replace("{troops}", Chronicler.Troops(sim.S.Get(Stat.Military)));
        }
    }
}
