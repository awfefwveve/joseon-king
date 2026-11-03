// 왕의 하루 — 데이터 정의
// 이 파일은 UnityEngine을 쓰지 않는다. 코어 로직은 유니티 밖에서도 돌아가게 둔다.
// 모든 클래스는 JsonUtility로 읽을 수 있게 public 필드 + [Serializable]로 둔다.
// 주의: JsonUtility는 JSON에 없는 필드를 0/null로 둔다. 그래서 "없으면 기본값" 규칙은 코드에서 처리한다.

using System;
using System.Collections.Generic;

namespace JoseonKing.Core
{
    public enum Stat
    {
        People,        // 민심
        Treasury,      // 국고
        Military,      // 군사
        Authority,     // 왕권
        Threat,        // 외침위협
        Health,        // 왕 건강
        HeirEducation, // 세자 학식
        EastFavor,     // 동인 호감
        WestFavor      // 서인 호감
    }

    public static class StatInfo
    {
        public static readonly Stat[] All =
        {
            Stat.People, Stat.Treasury, Stat.Military, Stat.Authority, Stat.Threat,
            Stat.Health, Stat.HeirEducation, Stat.EastFavor, Stat.WestFavor
        };

        public static string Name(Stat s)
        {
            switch (s)
            {
                case Stat.People: return "민심";
                case Stat.Treasury: return "국고";
                case Stat.Military: return "군사";
                case Stat.Authority: return "왕권";
                case Stat.Threat: return "외침위협";
                case Stat.Health: return "건강";
                case Stat.HeirEducation: return "세자학식";
                case Stat.EastFavor: return "동인";
                case Stat.WestFavor: return "서인";
            }
            return s.ToString();
        }

        // JSON에서 쓰는 키
        public static bool TryParse(string key, out Stat stat)
        {
            switch ((key ?? "").Trim().ToLowerInvariant())
            {
                case "people": stat = Stat.People; return true;
                case "treasury": stat = Stat.Treasury; return true;
                case "military": stat = Stat.Military; return true;
                case "authority": stat = Stat.Authority; return true;
                case "threat": stat = Stat.Threat; return true;
                case "health": stat = Stat.Health; return true;
                case "heir": stat = Stat.HeirEducation; return true;
                case "east": stat = Stat.EastFavor; return true;
                case "west": stat = Stat.WestFavor; return true;
            }
            stat = Stat.People;
            return false;
        }

        public static int Min(Stat s) => s == Stat.Treasury ? -100 : 0;
        public static int Max(Stat s) => s == Stat.Treasury ? 200 : 100;
    }

    [Serializable]
    public class StatDelta
    {
        public string stat;   // people, treasury, military, authority, threat, health, heir, east, west
        public int value;
    }

    // stat 키에는 수치 키 외에 front(전선), enemy(적 병력), age(왕 나이)도 쓸 수 있다.
    [Serializable]
    public class StatCondition
    {
        public string stat;
        public string op;     // ">=", ">", "<=", "<", "=="
        public int value;
    }

    [Serializable]
    public class PolicyDef
    {
        public string id;
        public string name;
        public string description;
        public StatDelta[] effects;
        public int cooldown;        // 0이면 기본 2계절
        public bool revealsThreat;  // 통신사 파견처럼 외침위협을 정확히 보여줌
        public string command;      // 임금이 직접 하교할 때 문장 ("세금을 줄여 백성의 짐을 덜라")
        public StatCondition[] agenda; // 대신이 이 안건을 올리는 조건. 비면 평시 안건
    }

    // 대본 대사 한 줄. 경우의 수는 (종류, 대신, 어명, 정황)으로 나뉜다.
    // kind: propose(안건 올림), opinion(의견), approved(윤허 뒤 반응), rejected(불허 뒤 반응)
    // minister / policy 가 "*" 이면 아무나·아무 어명. context 가 ""이면 평시.
    // context: war(전쟁 중), poor(곳간 빔), unrest(민심 흉흉), weak(왕권 약함)
    // 글자 치환: {name} 말하는 대신, {policy} 어명 이름, {rice} 곳간 쌀, {troops} 군사 수
    [Serializable]
    public class DialogueLine
    {
        public string kind;
        public string minister;
        public string policy;
        public string context;
        public string stance;   // rejected/approved 반응에서만 씀: support, oppose, neutral, 비면 아무나
        public string text;
    }

    [Serializable] public class DialogueList { public DialogueLine[] items; }

    [Serializable]
    public class MinisterDef
    {
        public string id;
        public string name;
        public string faction;      // east(동인), west(서인), none(무관)
        public string persona;      // LLM에게 줄 인물 설명 (알려진 행적만)
        public string speechStyle;  // 말투
        public string[] supports;   // 지지하는 어명 ID
        public string[] opposes;    // 반대하는 어명 ID
        public int leaveYear;       // 0이면 퇴장 없음. 이 해부터 자리에 없음
        public string color;        // 회색 박스 단계에서 캡슐 색 (#RRGGBB)
    }

    [Serializable]
    public class EventChoice
    {
        public string text;
        public StatDelta[] effects;
        public string[] setFlags;
        public string[] clearFlags;
        public string action;       // "", start_war, flee, end_war, quiet_threat
        public string result;       // 고른 뒤 보여줄 한 줄
    }

    [Serializable]
    public class EventDef
    {
        public string id;
        public string title;
        public string body;
        public string source;        // 실록 출처나 "전해지는 이야기" 표기
        public int minYear;          // 0이면 제한 없음
        public int maxYear;          // 0이면 제한 없음
        public string season;        // "", spring, summer, autumn, winter
        public float chance;         // 0 이하이면 1(항상)
        public bool once;
        public int priority;         // 높을수록 먼저. 역사 사건은 100
        public string[] requireFlags;
        public string[] forbidFlags;
        public StatCondition[] conditions;
        public string requireMinister; // 이 대신이 자리에 있어야 뜸
        public EventChoice[] choices;
    }

    // JsonUtility는 최상위 배열을 못 읽어서 감싼다.
    [Serializable] public class PolicyList { public PolicyDef[] items; }
    [Serializable] public class MinisterList { public MinisterDef[] items; }
    [Serializable] public class EventList { public EventDef[] items; }

    public class GameDatabase
    {
        public readonly List<PolicyDef> Policies = new List<PolicyDef>();
        public readonly List<MinisterDef> Ministers = new List<MinisterDef>();
        public readonly List<EventDef> Events = new List<EventDef>();
        public readonly List<DialogueLine> Dialogue = new List<DialogueLine>();

        public PolicyDef FindPolicy(string id) => Policies.Find(p => p.id == id);
        public MinisterDef FindMinister(string id) => Ministers.Find(m => m.id == id);

        // 데이터 오류를 미리 찾는다. 반환값이 비어 있으면 정상.
        public List<string> Validate()
        {
            var errors = new List<string>();
            foreach (var p in Policies)
            {
                if (string.IsNullOrEmpty(p.id)) errors.Add("어명 ID 없음: " + p.name);
                CheckDeltas(p.effects, "어명 " + p.id, errors);
            }
            foreach (var m in Ministers)
            {
                foreach (var id in m.supports ?? new string[0])
                    if (FindPolicy(id) == null) errors.Add($"대신 {m.id}: 없는 어명 {id}");
                foreach (var id in m.opposes ?? new string[0])
                    if (FindPolicy(id) == null) errors.Add($"대신 {m.id}: 없는 어명 {id}");
            }
            foreach (var e in Events)
            {
                if (e.choices == null || e.choices.Length == 0) errors.Add("사건 선택지 없음: " + e.id);
                foreach (var c in e.choices ?? new EventChoice[0])
                    CheckDeltas(c.effects, "사건 " + e.id, errors);
                if (!string.IsNullOrEmpty(e.requireMinister) && FindMinister(e.requireMinister) == null)
                    errors.Add($"사건 {e.id}: 없는 대신 {e.requireMinister}");
            }
            foreach (var d in Dialogue)
            {
                if (d.minister != "*" && FindMinister(d.minister) == null) errors.Add($"대사: 없는 대신 {d.minister}");
                if (d.policy != "*" && FindPolicy(d.policy) == null) errors.Add($"대사: 없는 어명 {d.policy}");
            }
            // 경우의 수 빠짐 검사: 모든 대신 × 모든 어명에 의견 대사가 있어야 한다
            foreach (var m in Ministers)
                foreach (var p in Policies)
                    if (!Dialogue.Exists(d => d.kind == "opinion" && d.minister == m.id && d.policy == p.id && string.IsNullOrEmpty(d.context)))
                        errors.Add($"대사 빠짐: {m.name} × {p.name} 의견");
            foreach (var p in Policies)
                if (!Dialogue.Exists(d => d.kind == "propose" && d.policy == p.id))
                    errors.Add($"대사 빠짐: {p.name} 안건 올리는 말");
            return errors;
        }

        static void CheckDeltas(StatDelta[] deltas, string where, List<string> errors)
        {
            if (deltas == null) return;
            foreach (var d in deltas)
                if (!StatInfo.TryParse(d.stat, out _)) errors.Add($"{where}: 모르는 수치 키 '{d.stat}'");
        }
    }
}
