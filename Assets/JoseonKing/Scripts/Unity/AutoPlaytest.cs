// 자동 플레이테스트 촬영.
// 유니티 메뉴 [왕의 하루 > 자동 플레이테스트 촬영]을 누르면 Play가 시작되고,
// 게임이 스스로 하루를 진행하면서 주요 장면을 프로젝트 폴더의 PlaytestShots/ 에 PNG로 저장한 뒤 Play를 끈다.
// 마우스·키보드를 건드리지 않으니 촬영 중에 다른 일을 해도 된다 (Game 창만 보이게 두면 됨).

using System.Collections;
using System.IO;
using System.Linq;
using JoseonKing.Core;
using UnityEngine;

namespace JoseonKing.UnityLayer
{
    public class AutoPlaytest : MonoBehaviour
    {
        const string PrefKey = "JoseonKing.AutoPlaytest";

        // 에디터 메뉴가 켜 두면, Play 시작 때 한 번 읽고 바로 끈다
        public static bool Requested
        {
            get
            {
                bool on = PlayerPrefs.GetInt(PrefKey, 0) == 1;
                if (on) { PlayerPrefs.SetInt(PrefKey, 0); PlayerPrefs.Save(); }
                return on;
            }
        }

        public static void Request() { PlayerPrefs.SetInt(PrefKey, 1); PlayerPrefs.Save(); }

        GameBootstrap game;
        string dir;
        int shot;

        IEnumerator Start()
        {
            game = GetComponent<GameBootstrap>();
            dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "PlaytestShots");
            if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            Directory.CreateDirectory(dir);

            yield return new WaitForSeconds(1.5f);
            var sim = game.Sim;

            // 1. 첫 상참: 아침 보고 + 대신이 안건을 올림
            yield return Shot("01_morning_report");

            // 2. 모두에게 의견을 물음 (대신이 말할 때 조명이 켜짐)
            game.AskAll();
            yield return WaitIdle();
            yield return Shot("02_ministers_opinions");

            // 3. 한 대신을 지목해 따로 물음 (평시 안건이 남아 있을 때)
            var first = game.Actors.Values.FirstOrDefault(a => a.gameObject.activeSelf);
            if (first != null && sim.Phase == DayPhase.Council) { game.Address(first); yield return WaitIdle(); }

            // 4. 윤허 → 대신 반응
            game.Decide(true);
            yield return WaitIdle();
            yield return Shot("03_decree_reaction");

            // 5. 하교하기 목록 (다음 날 열어야 하므로 하루를 넘긴 뒤에 찍음)
            // 6. 실록 사건: 1575 동서분당이 뜨도록 해를 옮김
            JumpTo(1575, Season.Autumn);
            game.EndCouncil();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("04_event_east_west_split");

            game.ChooseEvent(2);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("05_evening");

            game.DoEvening("incognito");
            yield return new WaitForSeconds(0.6f);
            yield return Shot("06_season_report");

            // 7. 다음 날: 직접 하교하기 목록
            game.StartDay();
            yield return WaitIdle();
            game.OpenCommands();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("07_royal_commands");
            game.UI.HideCommands();

            // 8. 1592 임진왜란: 외적 위협이 쌓인 상태로 해를 옮김
            JumpTo(1592, Season.Spring);
            sim.S.Set(Stat.Threat, 70);
            game.EndCouncil();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("08_event_imjin_war");

            game.ChooseEvent(0);          // 도성을 지킨다
            yield return new WaitForSeconds(0.4f);
            game.DoEvening("rest");
            yield return new WaitForSeconds(0.6f);
            yield return Shot("09_war_report");

            // 9. 전쟁 중 아침: 적이 상주까지, 곳간이 비고 왕권이 흔들리고 옥체가 상함
            sim.S.Front = 52;
            sim.S.Set(Stat.Treasury, 6);
            sim.S.Set(Stat.Authority, 24);
            sim.S.Set(Stat.Health, 28);
            game.StartDay();
            yield return WaitIdle();
            yield return Shot("10_war_morning_crisis");

            game.AskAll();
            yield return WaitIdle();
            yield return Shot("11_war_council");

            // 10. 엔딩: 왕권이 무너져 반정
            game.Decide(false);
            yield return WaitIdle();
            game.EndCouncil();
            yield return new WaitForSeconds(0.4f);
            if (sim.Phase == DayPhase.Memorial) { game.ChooseEvent(0); yield return new WaitForSeconds(0.4f); }
            sim.S.Set(Stat.Authority, 0);   // 사건 선택이 왕권을 올릴 수 있어서 저녁 직전에 무너뜨림
            if (sim.Phase == DayPhase.Evening) game.DoEvening("rest");
            yield return new WaitForSeconds(0.8f);
            yield return Shot("12_ending_epilogue");

            Debug.Log($"[자동 플레이테스트] {shot}장 저장: {dir}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // 촬영용으로 세월을 건너뛴다. 나이·재위 기간·날짜 수도 같이 흘러야 화면 표기가 맞는다
        void JumpTo(int year, Season season)
        {
            var s = game.Sim.S;
            int seasons = (year - s.Year) * 4 + ((int)season - (int)s.Season);
            if (seasons <= 0) return;
            s.KingAge += year - s.Year;
            s.Year = year;
            s.Season = season;
            s.Day += seasons;
            s.SeasonsThisReign += seasons;
            s.TotalSeasonsReigned += seasons;
        }

        IEnumerator WaitIdle()
        {
            yield return null;
            float t = 0f;
            while (game.Busy && t < 15f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.4f);
        }

        // 화면을 한 프레임 그린 뒤 Game 창을 그대로 읽어 PNG로 저장 (UI 포함)
        // ScreenCapture 모듈 없이 기본 기능(ReadPixels)만 쓴다
        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            shot++;
            yield return new WaitForSeconds(0.5f);
        }
    }
}
