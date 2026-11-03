// 숫자를 사람의 말로 바꾸는 곳. 플레이어는 수치를 보지 못하고, 신하의 보고와 소문으로만 나라를 안다.
// - 곳간은 쌀 몇 석, 군사는 몇 명으로 (실제 단위로 환산한 게임용 값)
// - 민심과 외적은 보고하는 사람의 짐작이라 틀릴 수 있다. 왕권이 셀수록 아첨이 섞인다.
// - 미행을 하거나 통신사를 보내야 정확히 안다.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JoseonKing.Core
{
    public class ReportLine
    {
        public string Speaker;   // 호조, 병조, 승정원, 어의 …
        public string Text;
    }

    public static class Chronicler
    {
        // 게임 값 1 = 쌀 2,000석, 군사 1 = 1,000명 (실제 통계가 아니라 체감용 환산)
        public static string Rice(int treasury)
        {
            if (treasury < 0) return $"빌린 쌀 {Amount(-treasury * 2000)} 석";
            return $"{Amount(treasury * 2000)} 석";
        }

        public static string Troops(int military) => $"{Amount(Math.Max(0, military) * 1000)} 명";

        static string Amount(int n)
        {
            if (n <= 0) return "없음";
            int man = n / 10000, cheon = n % 10000 / 1000;
            if (man == 0) return $"{cheon}천";
            return cheon == 0 ? $"{man}만" : $"{man}만 {cheon}천";
        }

        // 임진왜란 때 왜군의 실제 북상로를 전선 값에 대응시킨다
        static readonly int[] RouteAt = { 0, 10, 22, 35, 50, 62, 78, 90, 100 };
        static readonly string[] RoutePlace = { "부산 앞바다", "동래", "밀양", "대구", "상주", "충주", "용인", "한강 남쪽", "도성" };

        public static string FrontPlace(int front)
        {
            string place = RoutePlace[0];
            for (int i = 0; i < RouteAt.Length; i++) if (front >= RouteAt[i]) place = RoutePlace[i];
            return place;
        }

        // ───────── 아침 보고(조보) ─────────

        public static List<ReportLine> MorningReport(Simulation sim, Random rng)
        {
            var s = sim.S;
            var lines = new List<ReportLine>();
            int flattery = (s.Get(Stat.Authority) - 50) / 6;   // 왕권이 세면 좋은 말만 올라온다

            // 호조: 회계라 비교적 정확
            int t = s.Get(Stat.Treasury);
            lines.Add(new ReportLine
            {
                Speaker = "호조",
                Text = t < 0 ? $"곳간이 바닥나 {Rice(t)}을 빌려 쓰고 있사옵니다. 관원 녹봉이 밀리고 있사옵니다."
                     : t < 15 ? $"곳간에 쌀이 {Rice(t)}뿐이옵니다. 한 철 버티기도 빠듯하옵니다."
                     : $"곳간에 쌀이 {Rice(t)} 있사옵니다."
            });

            // 병조
            int mil = s.Get(Stat.Military);
            lines.Add(new ReportLine
            {
                Speaker = "병조",
                Text = $"각 도의 군사를 헤아리니 {Troops(mil)}이옵니다." +
                       (mil < 25 ? " 장부에만 있고 실제로는 없는 군사가 많다 하옵니다." : mil > 70 ? " 훈련이 잘되어 있사옵니다." : "")
            });

            // 전쟁
            if (s.AtWar)
            {
                string place = FrontPlace(s.Front);
                string enemy = s.Enemy > 70 ? "적의 기세가 아직 등등하옵니다" : s.Enemy > 35 ? "적도 지쳐 가고 있사옵니다" : "적이 무너지기 시작하였사옵니다";
                lines.Add(new ReportLine { Speaker = "도원수", Text = $"적이 {place}까지 이르렀사옵니다. {enemy}." });
            }

            // 민심: 승정원이 전하는 소문 (오차 + 아첨) 또는 미행으로 직접 본 것
            int people = s.Get(Stat.People);
            if (sim.PeopleKnown)
                lines.Add(new ReportLine { Speaker = "미행", Text = "어젯밤 직접 보신 저잣거리 형편: " + PeopleWords(people) });
            else
            {
                int heard = people + rng.Next(-8, 9) + flattery;
                lines.Add(new ReportLine { Speaker = "승정원", Text = "민간의 소문을 전하옵니다. " + PeopleWords(heard) });
            }

            // 변방: 짐작 (통신사를 보냈으면 정확)
            if (!s.AtWar)
            {
                int threat = s.Get(Stat.Threat);
                int guess = sim.ThreatKnown ? threat : threat + rng.Next(-15, 16) - flattery;
                string who = sim.ThreatKnown ? "통신사" : "변방 장계";
                lines.Add(new ReportLine { Speaker = who, Text = ThreatWords(guess) });
            }

            // 어의 (건강이 나쁠 때만 먼저 아룀)
            int h = s.Get(Stat.Health);
            if (h < 60) lines.Add(new ReportLine { Speaker = "어의", Text = HealthWords(h) });

            // 조정 분위기
            string court = CourtWords(s);
            if (court != null) lines.Add(new ReportLine { Speaker = "조정", Text = court });

            // 세자
            if (s.HasHeir && rng.Next(4) == 0)
                lines.Add(new ReportLine { Speaker = "세자시강원", Text = HeirWords(s.Get(Stat.HeirEducation)) });

            return lines;
        }

        public static string PeopleWords(int v) =>
            v < 15 ? "고을마다 관아를 원망하는 벽서가 붙고, 무리를 지어 다니는 자들이 있다 하옵니다." :
            v < 30 ? "백성들이 수령을 원망하고, 유랑하는 자가 늘었다 하옵니다." :
            v < 50 ? "백성들이 살기 팍팍하다고들 하옵니다." :
            v < 70 ? "저잣거리가 평온하고 장사가 잘된다 하옵니다." :
            v < 85 ? "백성들이 성상의 덕을 칭송한다 하옵니다." :
                     "아이들까지 태평가를 부른다 하옵니다.";

        public static string ThreatWords(int v) =>
            v < 20 ? "변방이 조용하옵니다." :
            v < 40 ? "바다 건너 왜국의 형편이 심상치 않다는 풍문이 있사옵니다." :
            v < 60 ? "왜국이 배를 많이 만들고 군사를 모은다는 말이 들려오옵니다." :
            v < 80 ? "왜국이 조선을 칠 뜻을 공공연히 말한다 하옵니다. 대비하셔야 하옵니다." :
                     "왜의 대군이 곧 바다를 건널 것이라는 첩보가 잇따르옵니다!";

        public static string HealthWords(int v) =>
            v < 20 ? "옥체가 매우 위중하시옵니다. 정사를 쉬시옵소서." :
            v < 40 ? "맥이 약하고 기침이 잦으시옵니다. 무리하지 마시옵소서." :
                     "피로가 쌓이셨사옵니다. 조섭에 힘쓰시옵소서.";

        static string HeirWords(int v) =>
            v < 30 ? "세자께서 아직 글공부에 마음을 두지 않으시옵니다." :
            v < 70 ? "세자께서 경서를 착실히 익히고 계시옵니다." :
                     "세자께서 학문이 깊어 신하들이 감복하옵니다.";

        static string CourtWords(GameState s)
        {
            int east = s.Get(Stat.EastFavor), west = s.Get(Stat.WestFavor), a = s.Get(Stat.Authority);
            if (a < 25) return "대신들이 어명을 받고도 서로 눈치만 보옵니다. 조정 밖에서 은밀히 모이는 자들이 있다는 말도 있사옵니다.";
            if (east <= 20) return "동인들이 잇따라 사직을 청하며 조정에 나오지 않사옵니다.";
            if (west <= 20) return "서인들이 잇따라 사직을 청하며 조정에 나오지 않사옵니다.";
            if (Math.Abs(east - west) >= 35) return (east > west ? "동인" : "서인") + "이 요직을 거의 다 차지하여, 상대 당의 원망이 깊사옵니다.";
            if (a > 85) return "조정이 성상의 뜻에 감히 토를 달지 못하옵니다. 간언하는 자가 드물어졌사옵니다.";
            return null;
        }

        // ───────── 석 달 뒤 결산 ─────────

        public static string SeasonSummary(DayReport r, Simulation sim)
        {
            var sb = new StringBuilder();
            int t0 = r.Before[Stat.Treasury], t1 = r.After[Stat.Treasury];
            int m0 = r.Before[Stat.Military], m1 = r.After[Stat.Military];
            sb.AppendLine($"곳간: {Rice(t0)} → {Rice(t1)}");
            sb.AppendLine($"군사: {Troops(m0)} → {Troops(m1)}");

            int p0 = r.Before[Stat.People], p1 = r.After[Stat.People];
            if (PeopleWords(p0) != PeopleWords(p1))
                sb.AppendLine(p1 > p0 ? "민심이 한결 나아졌다는 말이 들린다." : "민심이 더 흉흉해졌다는 말이 들린다.");

            int h0 = r.Before[Stat.Health], h1 = r.After[Stat.Health];
            if (h1 < 40 && h1 < h0) sb.AppendLine("옥체가 눈에 띄게 쇠약해졌다.");

            if (sim.S.NegativeTreasurySeasons > 0)
                sb.AppendLine("호조판서가 사색이 되어 아뢴다. 이대로 가면 한두 철 안에 나라 살림이 무너진다고.");
            return sb.ToString();
        }

        // ───────── 마지막: 사관의 논평 ─────────

        public static string Epilogue(Simulation sim)
        {
            var s = sim.S;
            var sb = new StringBuilder();
            sb.AppendLine(Simulation.EndingTitle(s.Ending));
            sb.AppendLine();
            sb.AppendLine(EndingNarration(s.Ending, s));
            sb.AppendLine();
            sb.Append("사관은 논한다. ");
            sb.Append(s.Get(Stat.People) >= 60 ? "백성이 그 덕을 입었고, " : s.Get(Stat.People) < 30 ? "백성의 원망이 하늘에 닿았고, " : "백성의 삶은 고단하였으나 버틸 만하였고, ");
            sb.Append(s.Get(Stat.Treasury) >= 40 ? "나라 곳간은 넉넉하였다. " : s.Get(Stat.Treasury) < 0 ? "나라 곳간은 바닥났다. " : "나라 살림은 빠듯하였다. ");
            sb.Append(s.Get(Stat.Authority) >= 70 ? "임금의 위엄이 조정을 눌렀다." : s.Get(Stat.Authority) < 30 ? "신하들이 임금을 가벼이 여겼다." : "임금과 신하가 서로 견제하였다.");
            sb.AppendLine();
            sb.AppendLine();
            var kings = new List<string>(s.PastKings) { $"{s.KingName} (재위 {s.SeasonsThisReign / 4}년)" };
            sb.AppendLine("왕계: " + string.Join(" → ", kings));
            sb.Append($"1567년부터 {s.Year}년까지, {s.TotalSeasonsReigned / 4}년.");
            return sb.ToString();
        }

        static string EndingNarration(Ending e, GameState s)
        {
            switch (e)
            {
                case Ending.Flight: return "임금이 도성을 버리고 북쪽으로 몸을 피하니, 백성들이 길을 막고 통곡하였다. 버려진 도성은 불탔고, 임금의 권위도 함께 무너졌다.";
                case Ending.PalaceFallen: return $"적이 {FrontPlace(100)}에 들이닥쳤다. 궁궐의 문이 부서지고, 사직은 끊어졌다.";
                case Ending.Revolt: return "굶주린 백성들이 들고일어나 관아를 불태우고 도성까지 밀려들었다. 군사들마저 창을 거꾸로 들었다.";
                case Ending.Deposed: return "어느 밤, 대신들이 군사를 이끌고 대궐에 들어와 대비의 교지를 받들어 임금을 폐하였다.";
                case Ending.Bankrupt: return "녹봉도 군량도 댈 수 없게 되자 관원들은 흩어지고, 군사들은 무기를 버렸다. 나라는 껍데기만 남았다.";
                case Ending.NoHeir: return "임금이 후사 없이 승하하니, 종친 가운데서 새 임금을 추대하였다. 이 왕계는 여기서 끝난다.";
                case Ending.Completed: return "숱한 고비를 넘기며 사직을 지켜냈다. 역사와 다른 길을 걸은 왕조의 이야기가 여기서 끝을 맺는다.";
            }
            return "";
        }
    }
}
