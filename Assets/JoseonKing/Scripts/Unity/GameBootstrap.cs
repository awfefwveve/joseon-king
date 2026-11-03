// 왕의 하루 — 시작점.
// 아무 씬에서나 Play를 누르면 저절로 생긴다 (AutoStart). 에셋 슬롯을 쓰려면
// 빈 GameObject에 이 컴포넌트를 직접 붙이고 인스펙터에서 프리팹을 넣으면 된다.
// 대사는 전부 대본(dialogue.json)이다. 서버나 LLM을 쓰지 않으므로 동접자 수와 비용이 무관하다.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JoseonKing.Core;
using UnityEngine;

namespace JoseonKing.UnityLayer
{
    public class GameBootstrap : MonoBehaviour
    {
        [Header("에셋 슬롯 (비우면 회색 박스)")]
        public CourtAssetSlots assets = new CourtAssetSlots();

        [Header("기타")]
        [Tooltip("비우면 OS 한글 폰트(맑은 고딕 등)를 찾아 쓴다")]
        public Font uiFont;
        [Tooltip("0이면 매번 다른 판")]
        public int seed = 0;
        [Tooltip("이 해가 끝나면 왕조 완주 (실제 조선의 마지막 해 1910)")]
        public int endYear = 1910;
        [Tooltip("대신이 한 줄 말하고 다음 대신이 말하기까지 걸리는 시간(초)")]
        public float lineDelay = 0.9f;

        Simulation sim;
        ScriptedCouncil council;
        CourtUI ui;

        // 자동 플레이테스트(AutoPlaytest)가 쓰는 창구
        public Simulation Sim => sim;
        public CourtUI UI => ui;
        public bool Busy => busy;
        public IReadOnlyDictionary<string, MinisterActor> Actors => actors;
        Transform worldRoot;
        Dictionary<string, MinisterActor> actors = new Dictionary<string, MinisterActor>();
        System.Random rng;

        readonly HashSet<AgendaItem> introduced = new HashSet<AgendaItem>();
        string addressedId;
        bool busy;
        string eveningMessage = "";

        // 씬에 GameBootstrap이 없으면 Play 때 저절로 하나 만든다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (FindFirstObjectByType<GameBootstrap>() == null)
                new GameObject("왕의 하루").AddComponent<GameBootstrap>();
        }

        // ───────── 시작 ─────────

        void Start()
        {
            if (uiFont == null) uiFont = FindKoreanFont();
            if (AutoPlaytest.Requested) { seed = 20261103; gameObject.AddComponent<AutoPlaytest>(); }

            ui = new CourtUI(uiFont);
            ui.OnAsk += AskAll;
            ui.OnApprove += () => Decide(true);
            ui.OnReject += () => Decide(false);
            ui.OnOpenCommands += OpenCommands;
            ui.OnCommand += RaiseCommand;
            ui.OnEndCouncil += EndCouncil;
            ui.OnChoice += ChooseEvent;
            ui.OnEvening += DoEvening;
            ui.OnNextDay += StartDay;
            ui.OnRestart += NewGame;

            NewGame();
        }

        void Update()
        {
            if (InputCompat.F1Down && ui != null) ui.ToggleDebug();
        }

        void NewGame()
        {
            if (worldRoot != null) Destroy(worldRoot.gameObject);
            worldRoot = new GameObject("편전").transform;
            introduced.Clear();
            addressedId = null;

            var db = DataLoader.Load();
            int s = seed != 0 ? seed : System.Environment.TickCount;
            rng = new System.Random(s);
            sim = new Simulation(db, s) { EndYear = endYear };
            council = new ScriptedCouncil(sim, rng);

            var builder = new CourtSceneBuilder(worldRoot, assets, uiFont);
            var cam = builder.BuildRoomAndCamera();
            var throneCam = cam.GetComponent<ThroneCamera>();
            throneCam.OnMinisterClicked -= Address;
            throneCam.OnMinisterClicked += Address;
            actors = builder.BuildMinisters(db.Ministers);

            ui.ClearLog();
            ui.Log("오른쪽 마우스를 누른 채 움직이면 고개를 돌립니다. 대신을 클릭하면 그 대신에게 따로 묻습니다.", CourtUI.Quiet);
            ui.SetAddressed(null);
            StartDay();
        }

        // ───────── 하루: 상참 ─────────

        public void StartDay()
        {
            if (sim.Phase == DayPhase.Ended) { ShowEnding(); return; }
            RefreshMinisters();
            RefreshScreen();
            ui.ShowCouncil();

            ui.Log($"── {sim.S.DateText} · 상참 ──", CourtUI.Gold);
            foreach (var line in Chronicler.MorningReport(sim, rng))
                ui.Log($"[{line.Speaker}] {line.Text}", CourtUI.Quiet);

            council.BeginDay();
            ShowCurrentAgenda();
        }

        void ShowCurrentAgenda()
        {
            var item = council.Current;
            if (item == null) { RefreshAgendaBar(); return; }

            if (!introduced.Contains(item))
            {
                introduced.Add(item);
                if (!item.RaisedByKing)
                {
                    var line = council.ProposeLine(item);
                    Say(line);
                }
            }
            RefreshAgendaBar();
        }

        void RefreshAgendaBar()
        {
            var item = council.Current;
            if (item == null) { ui.SetAgenda(null, false, !sim.S.DecreedToday, busy); return; }
            var p = sim.Db.FindPolicy(item.PolicyId);
            string who = item.RaisedByKing ? "임금의 하교" : sim.Db.FindMinister(item.ProposerId)?.name + "의 청";
            string label = $"안건: {p.name}  ({who})";
            if (sim.S.DecreedToday) label += "  — 오늘은 이미 큰 결정을 내렸다";
            ui.SetAgenda(label, item.RaisedByKing, !sim.S.DecreedToday, busy);
        }

        public void AskAll()
        {
            var item = council.Current;
            if (item == null || busy) return;
            ui.Log("임금: 경들의 생각은 어떠한가.", CourtUI.KingColor);
            var lines = council.AskAll(item);
            if (lines.Count == 0) { ui.Log("(더 아뢰는 대신이 없다)", CourtUI.Quiet); return; }
            StartCoroutine(SpeakInTurn(lines));
        }

        public void Address(MinisterActor actor)
        {
            addressedId = actor.Def.id;
            foreach (var a in actors.Values) a.SetSelected(a == actor);
            ui.SetAddressed(actor.Def.name);

            var item = council.Current;
            if (item == null || busy || sim.Phase != DayPhase.Council) return;
            ui.Log($"임금: {actor.Def.name}, 경은 어찌 생각하는가.", CourtUI.KingColor);
            Say(council.AskOne(item, actor.Def.id));
        }

        public void Decide(bool approve)
        {
            var item = council.Current;
            if (item == null || busy) return;
            var p = sim.Db.FindPolicy(item.PolicyId);

            if (approve)
            {
                ui.Log(item.RaisedByKing ? "임금: 그리 시행하라." : "임금: 윤허한다.", CourtUI.KingColor);
                ui.Log(sim.ApplyPolicy(item.PolicyId), CourtUI.Gold);
                foreach (var a in actors.Values) a.Bow();
            }
            else if (sim.S.DecreedToday)
            {
                ui.Log($"임금: {p.name} 건은 다음에 다시 논의하자.", CourtUI.KingColor);
            }
            else
            {
                ui.Log(item.RaisedByKing ? "임금: 내 말을 거두겠다." : "임금: 윤허하지 않는다.", CourtUI.KingColor);
                if (!item.RaisedByKing) sim.RejectProposal(item.PolicyId, item.ProposerId);
            }

            var reactions = sim.S.DecreedToday && !approve ? new List<CouncilLine>() : council.React(item, approve);
            council.Next();
            StartCoroutine(SpeakInTurn(reactions, () => ShowCurrentAgenda()));
            RefreshScreen();
        }

        public void OpenCommands()
        {
            if (busy || sim.S.DecreedToday) return;
            var list = sim.Db.Policies.Select(p => (p.id, p.command, sim.IsPolicyReady(p))).ToList();
            ui.ShowCommands(list);
        }

        void RaiseCommand(string policyId)
        {
            ui.HideCommands();
            var p = sim.Db.FindPolicy(policyId);
            council.RaiseByKing(policyId);
            ui.Log("임금: " + p.command, CourtUI.KingColor);
            ShowCurrentAgenda();
        }

        public void EndCouncil()
        {
            if (busy) return;
            ui.Log("임금: 오늘은 이만 파하라.", CourtUI.KingColor);
            sim.EndCouncil();
            if (sim.Phase == DayPhase.Memorial && sim.CurrentEvent != null)
            {
                var e = sim.CurrentEvent;
                ui.ShowEvent(e.title, e.body, e.source, e.choices.Select(c => c.text).ToList());
            }
            else
            {
                ui.Log("오늘은 올라온 상소가 없다.", CourtUI.Quiet);
                ui.ShowEvening(sim.CanDo(EveningAction.EducateHeir));
            }
        }

        // ───────── 상소, 저녁, 결산 ─────────

        public void ChooseEvent(int index)
        {
            string result = sim.ChooseEventOption(index);
            ui.Log(result, CourtUI.Gold);
            RefreshScreen();
            if (sim.Phase == DayPhase.Ended) { ShowEnding(); return; }
            ui.ShowEvening(sim.CanDo(EveningAction.EducateHeir));
        }

        public void DoEvening(string key)
        {
            EveningAction action;
            switch (key)
            {
                case "educate": action = EveningAction.EducateHeir; break;
                case "banquet": action = EveningAction.Banquet; break;
                case "incognito": action = EveningAction.Incognito; break;
                default: action = EveningAction.Rest; break;
            }
            if (!sim.CanDo(action)) return;
            eveningMessage = sim.DoEvening(action);
            var report = sim.EndDay();
            RefreshScreen();

            if (sim.Phase == DayPhase.Ended) { ShowEnding(); return; }
            ui.ShowReport(ReportText(report));
        }

        string ReportText(DayReport r)
        {
            var sb = new StringBuilder();
            sb.AppendLine(eveningMessage);
            sb.AppendLine();
            sb.AppendLine("<color=#EECC80>석 달이 흘렀다.</color>");
            sb.AppendLine($"<color=#EECC80><size=28>{sim.S.DateText}</size></color>");
            sb.AppendLine();
            foreach (var line in r.Lines) sb.AppendLine(line);
            sb.Append(Chronicler.SeasonSummary(r, sim));
            return sb.ToString();
        }

        void ShowEnding()
        {
            RefreshScreen();
            ui.ShowEnding(Chronicler.Epilogue(sim));
        }

        // ───────── 대사 연출 ─────────

        void Say(CouncilLine line)
        {
            if (line == null) return;
            var def = sim.Db.FindMinister(line.MinisterId);
            if (def == null) { ui.Log(line.Text, CourtUI.Quiet); return; }
            ui.Log($"{def.name}: {line.Text}");
            if (actors.TryGetValue(def.id, out var a)) a.Speak();
        }

        // 대신들이 한 사람씩 차례로 아뢴다
        IEnumerator SpeakInTurn(List<CouncilLine> lines, System.Action after = null)
        {
            busy = true;
            RefreshAgendaBar();
            foreach (var l in lines)
            {
                Say(l);
                yield return new WaitForSeconds(lineDelay);
            }
            busy = false;
            if (after != null) after(); else RefreshAgendaBar();
        }

        // ───────── 화면 갱신 ─────────

        void RefreshMinisters()
        {
            var active = sim.ActiveMinisters().Select(m => m.id).ToHashSet();
            foreach (var kv in actors) kv.Value.gameObject.SetActive(active.Contains(kv.Key));
            if (addressedId != null && !active.Contains(addressedId))
            {
                addressedId = null;
                ui.SetAddressed(null);
            }
        }

        void RefreshScreen()
        {
            var s = sim.S;
            ui.SetDate(s.DateText + (s.AtWar ? "  ·  전란 중" : ""));
            ui.SetHealthVeil(s.Get(Stat.Health));

            // F1 개발용: 실제 수치
            var sb = new StringBuilder("[개발용 수치 — F1]\n");
            foreach (var st in StatInfo.All) sb.Append($"{StatInfo.Name(st)} {s.Get(st)}  ");
            sb.Append($"\n전선 {s.Front} 적 {s.Enemy}  어명 시행률 {sim.ExecutionRate():P0}  곳간 적자 {s.NegativeTreasurySeasons}계절  {s.Generation}대");
            ui.SetDebug(sb.ToString());
        }

        static Font FindKoreanFont()
        {
            var os = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "AppleGothic", "Noto Sans CJK KR", "NanumGothic" }, 24);
            if (os != null) return os;
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
