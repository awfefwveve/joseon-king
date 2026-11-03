using System.IO;
using JoseonKing.Core;
using UnityEngine;

namespace JoseonKing.UnityLayer
{
    // StreamingAssets/JoseonKing/*.json 을 읽는다. (PC 빌드 기준. 모바일은 UnityWebRequest로 바꿔야 함)
    public static class DataLoader
    {
        public static GameDatabase Load()
        {
            var db = new GameDatabase();
            string dir = Path.Combine(Application.streamingAssetsPath, "JoseonKing");

            var policies = Read<PolicyList>(Path.Combine(dir, "policies.json"));
            var ministers = Read<MinisterList>(Path.Combine(dir, "ministers.json"));
            var events = Read<EventList>(Path.Combine(dir, "events.json"));
            var dialogue = Read<DialogueList>(Path.Combine(dir, "dialogue.json"));

            if (policies?.items != null) db.Policies.AddRange(policies.items);
            if (ministers?.items != null) db.Ministers.AddRange(ministers.items);
            if (events?.items != null) db.Events.AddRange(events.items);
            if (dialogue?.items != null) db.Dialogue.AddRange(dialogue.items);

            foreach (var err in db.Validate()) Debug.LogError("[데이터 오류] " + err);
            Debug.Log($"[왕의 하루] 어명 {db.Policies.Count}, 대신 {db.Ministers.Count}, 사건 {db.Events.Count}, 대사 {db.Dialogue.Count} 불러옴");
            return db;
        }

        static T Read<T>(string path) where T : class
        {
            if (!File.Exists(path))
            {
                Debug.LogError("[왕의 하루] 데이터 파일 없음: " + path);
                return null;
            }
            return JsonUtility.FromJson<T>(File.ReadAllText(path));
        }
    }
}
