// 유니티 위쪽 메뉴에 [왕의 하루 > 자동 플레이테스트 촬영]을 추가한다.
using JoseonKing.UnityLayer;
using UnityEditor;

namespace JoseonKing.EditorTools
{
    public static class PlaytestMenu
    {
        [MenuItem("왕의 하루/자동 플레이테스트 촬영")]
        static void Run()
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            AutoPlaytest.Request();
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("왕의 하루/촬영 폴더 열기")]
        static void OpenFolder()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath), "PlaytestShots");
            System.IO.Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }
    }
}
