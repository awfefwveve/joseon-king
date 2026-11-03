using System;
using System.Collections.Generic;

namespace JoseonKing.Core
{
    public enum Season { Spring = 0, Summer = 1, Autumn = 2, Winter = 3 }

    public enum Ending
    {
        None,
        Flight,        // 몽진 — 피난한 순간 패배
        PalaceFallen,  // 궁궐 함락
        Revolt,        // 민란
        Deposed,       // 반정
        Bankrupt,      // 재정파탄
        NoHeir,        // 후사 없이 승하
        Completed      // 정해진 마지막 해까지 왕조를 지켜냄
    }

    public class GameState
    {
        // 시간
        public int Year = 1567;
        public Season Season = Season.Autumn;
        public int Day = 1;              // 누적 날짜(= 누적 계절 수)

        // 왕
        public string KingName = "선조";
        public int AccessionYear = 1567; // 즉위한 해. 이듬해가 원년(유년칭원법)
        public int KingAge = 16;
        public int Generation = 1;       // 몇 대째 왕인지
        public int SeasonsThisReign = 0;
        public int TotalSeasonsReigned = 0;
        public bool HasHeir => Flags.Contains("has_heir");

        // 전쟁
        public bool AtWar;
        public int Front;                // 0~100. 100이면 궁궐 함락
        public int Enemy;                // 적 병력

        // 기타 상태
        public int NegativeTreasurySeasons;
        public bool ThreatKnown;         // 통신사 등으로 외침위협을 정확히 앎
        public bool DecreedToday;
        public Ending Ending = Ending.None;

        public readonly Dictionary<Stat, int> Stats = new Dictionary<Stat, int>();
        public readonly HashSet<string> Flags = new HashSet<string>();
        public readonly HashSet<string> FiredOnce = new HashSet<string>();
        public readonly Dictionary<string, int> PolicyReadyDay = new Dictionary<string, int>();
        public readonly List<string> Chronicle = new List<string>();   // 실록처럼 쌓이는 기록
        public readonly List<string> PastKings = new List<string>();

        public GameState()
        {
            Stats[Stat.People] = 55;
            Stats[Stat.Treasury] = 60;
            Stats[Stat.Military] = 35;
            Stats[Stat.Authority] = 45;
            Stats[Stat.Threat] = 10;
            Stats[Stat.Health] = 85;
            Stats[Stat.HeirEducation] = 0;
            Stats[Stat.EastFavor] = 50;
            Stats[Stat.WestFavor] = 50;
        }

        public int Get(Stat s) => Stats[s];

        public void Set(Stat s, int v) => Stats[s] = Math.Max(StatInfo.Min(s), Math.Min(StatInfo.Max(s), v));

        public void Add(Stat s, int delta) => Set(s, Get(s) + delta);

        // 조건 검사용. 수치 키 외에 front, enemy, age도 받는다.
        public int GetValue(string key)
        {
            if (StatInfo.TryParse(key, out var s)) return Get(s);
            switch ((key ?? "").ToLowerInvariant())
            {
                case "front": return Front;
                case "enemy": return Enemy;
                case "age": return KingAge;
                case "year": return Year;
                case "faction_gap": return Math.Abs(Get(Stat.EastFavor) - Get(Stat.WestFavor));
            }
            return 0;
        }

        public string SeasonName
        {
            get
            {
                switch (Season)
                {
                    case Season.Spring: return "봄";
                    case Season.Summer: return "여름";
                    case Season.Autumn: return "가을";
                    default: return "겨울";
                }
            }
        }

        // 실록 표기: 선조 즉위년(1567) 가을. 즉위 이듬해가 원년이다.
        public string ReignYearText => Year == AccessionYear ? "즉위년" : Year - AccessionYear == 1 ? "원년" : $"{Year - AccessionYear}년";
        public string DateText => $"{KingName} {ReignYearText}({Year}) {SeasonName} · 보령 {KingAge}세";

        public void Record(string line) => Chronicle.Add($"[{Year} {SeasonName}] {line}");
    }
}
