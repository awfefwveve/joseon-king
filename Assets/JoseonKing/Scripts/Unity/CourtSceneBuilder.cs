// 편전을 코드로 세운다. 에셋 슬롯이 비어 있으면 회색 박스, 프리팹을 넣으면 그걸 쓴다.
// 좌표 기준: 원점 = 편전 바닥 중앙. 용상은 +Z(북쪽), 대신들은 그 앞에 마주 서 있다.

using System;
using System.Collections.Generic;
using JoseonKing.Core;
using UnityEngine;

namespace JoseonKing.UnityLayer
{
    [Serializable]
    public class MinisterPrefabSlot
    {
        public string ministerId;   // ministers.json의 id (예: yi_i)
        public GameObject prefab;
    }

    [Serializable]
    public class CourtAssetSlots
    {
        [Tooltip("편전 내부 전체. 원점 = 용상 앞 바닥 중앙. 비우면 회색 박스 방")]
        public GameObject palacePrefab;
        [Tooltip("용상 + 일월오봉도. 비우면 박스 의자")]
        public GameObject thronePrefab;
        [Tooltip("대신별 프리팹. 없는 대신은 파벌 색 캡슐")]
        public MinisterPrefabSlot[] ministers;
    }

    public class CourtSceneBuilder
    {
        public static readonly Vector3 ThronePos = new Vector3(0f, 0f, 7f);
        public const float DaisHeight = 0.8f;

        // 대신 자리: 좌우 두 줄
        static readonly Vector3[] Spots =
        {
            new Vector3(-2.2f, 0, 3.0f), new Vector3(2.2f, 0, 3.0f),
            new Vector3(-2.2f, 0, 1.4f), new Vector3(2.2f, 0, 1.4f),
            new Vector3(-2.2f, 0, -0.2f), new Vector3(2.2f, 0, -0.2f),
            new Vector3(-2.2f, 0, -1.8f), new Vector3(2.2f, 0, -1.8f)
        };

        readonly Transform root;
        readonly CourtAssetSlots slots;
        readonly Font font;

        public CourtSceneBuilder(Transform root, CourtAssetSlots slots, Font font)
        {
            this.root = root;
            this.slots = slots ?? new CourtAssetSlots();
            this.font = font;
        }

        public Camera BuildRoomAndCamera()
        {
            if (slots.palacePrefab != null) UnityEngine.Object.Instantiate(slots.palacePrefab, Vector3.zero, Quaternion.identity, root);
            else BuildGreyboxRoom();

            if (slots.thronePrefab != null) UnityEngine.Object.Instantiate(slots.thronePrefab, ThronePos, Quaternion.identity, root);
            else BuildGreyboxThrone();

            BuildLights();
            return BuildCamera();
        }

        public Dictionary<string, MinisterActor> BuildMinisters(IList<MinisterDef> ministers)
        {
            var result = new Dictionary<string, MinisterActor>();
            for (int i = 0; i < ministers.Count; i++)
            {
                var def = ministers[i];
                var pos = Spots[i % Spots.Length];
                var facing = Quaternion.LookRotation(new Vector3(ThronePos.x - pos.x, 0, ThronePos.z - pos.z));

                GameObject go;
                var prefab = FindPrefab(def.id);
                if (prefab != null)
                {
                    go = UnityEngine.Object.Instantiate(prefab, pos, facing, root);
                }
                else
                {
                    go = new GameObject();
                    go.transform.SetParent(root, false);
                    go.transform.SetPositionAndRotation(pos, facing);
                    var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    body.transform.SetParent(go.transform, false);
                    body.transform.localPosition = new Vector3(0, 0.9f, 0);
                    body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                    Paint(body, ParseColor(def.color, FactionColor(def.faction)));
                    // 관모 대신 작은 박스
                    var hat = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    hat.transform.SetParent(go.transform, false);
                    hat.transform.localPosition = new Vector3(0, 1.85f, 0);
                    hat.transform.localScale = new Vector3(0.35f, 0.18f, 0.3f);
                    Paint(hat, new Color(0.1f, 0.1f, 0.1f));
                    UnityEngine.Object.Destroy(hat.GetComponent<Collider>());
                }
                go.name = "대신_" + def.name;
                var actor = go.AddComponent<MinisterActor>();
                actor.Init(def, font, ParseColor(def.color, FactionColor(def.faction)), 2.25f);
                result[def.id] = actor;
            }
            return result;
        }

        GameObject FindPrefab(string id)
        {
            if (slots.ministers == null) return null;
            foreach (var s in slots.ministers) if (s != null && s.ministerId == id) return s.prefab;
            return null;
        }

        void BuildGreyboxRoom()
        {
            var floorColor = new Color(0.45f, 0.38f, 0.30f);
            var wallColor = new Color(0.62f, 0.25f, 0.20f);   // 붉은 칠
            var pillarColor = new Color(0.55f, 0.18f, 0.15f);

            Box("바닥", new Vector3(0, -0.05f, 2f), new Vector3(14f, 0.1f, 18f), floorColor);
            Box("북쪽 벽", new Vector3(0, 3f, 10.5f), new Vector3(14f, 6f, 0.3f), wallColor);
            Box("남쪽 벽", new Vector3(0, 3f, -7f), new Vector3(14f, 6f, 0.3f), wallColor);
            Box("동쪽 벽", new Vector3(7f, 3f, 2f), new Vector3(0.3f, 6f, 18f), wallColor);
            Box("서쪽 벽", new Vector3(-7f, 3f, 2f), new Vector3(0.3f, 6f, 18f), wallColor);
            Box("천장", new Vector3(0, 6.05f, 2f), new Vector3(14f, 0.1f, 18f), new Color(0.3f, 0.45f, 0.4f));

            for (int i = 0; i < 4; i++)
            {
                float z = -4f + i * 4f;
                Box("기둥", new Vector3(-4.5f, 3f, z), new Vector3(0.5f, 6f, 0.5f), pillarColor);
                Box("기둥", new Vector3(4.5f, 3f, z), new Vector3(0.5f, 6f, 0.5f), pillarColor);
            }
            // 품계석 대신 바닥 표시
            for (int i = 0; i < 4; i++)
            {
                Box("품계석", new Vector3(-1.4f, 0.1f, 3f - i * 1.6f), new Vector3(0.25f, 0.2f, 0.25f), Color.gray);
                Box("품계석", new Vector3(1.4f, 0.1f, 3f - i * 1.6f), new Vector3(0.25f, 0.2f, 0.25f), Color.gray);
            }
        }

        void BuildGreyboxThrone()
        {
            Box("어좌 단", ThronePos + new Vector3(0, DaisHeight / 2f, 0.3f), new Vector3(4f, DaisHeight, 3.2f), new Color(0.5f, 0.2f, 0.15f));
            Box("용상 좌판", ThronePos + new Vector3(0, DaisHeight + 0.25f, 0.4f), new Vector3(1.2f, 0.5f, 0.9f), new Color(0.7f, 0.5f, 0.15f));
            Box("용상 등받이", ThronePos + new Vector3(0, DaisHeight + 0.9f, 0.85f), new Vector3(1.2f, 1.2f, 0.15f), new Color(0.7f, 0.5f, 0.15f));
            // 일월오봉도 자리
            Box("일월오봉도", ThronePos + new Vector3(0, DaisHeight + 1.6f, 1.6f), new Vector3(4.2f, 2.6f, 0.1f), new Color(0.2f, 0.35f, 0.5f));
        }

        void BuildLights()
        {
            var sun = new GameObject("주광");
            sun.transform.SetParent(root, false);
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var l = sun.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 0.6f;
            l.color = new Color(1f, 0.95f, 0.85f);

            var warm = new GameObject("실내등");
            warm.transform.SetParent(root, false);
            warm.transform.position = new Vector3(0, 4.5f, 2f);
            var p = warm.AddComponent<Light>();
            p.type = LightType.Point;
            p.range = 18f;
            p.intensity = 1.5f;
            p.color = new Color(1f, 0.8f, 0.6f);

            RenderSettings.ambientLight = new Color(0.35f, 0.32f, 0.3f);
        }

        Camera BuildCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            // 용상에 앉은 눈높이, 남쪽(대신들)을 내려다봄
            cam.transform.position = ThronePos + new Vector3(0, DaisHeight + 1.25f, 0.4f);
            cam.transform.rotation = Quaternion.Euler(12f, 180f, 0f);
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.07f, 0.06f);
            if (cam.GetComponent<ThroneCamera>() == null) cam.gameObject.AddComponent<ThroneCamera>();
            return cam;
        }

        GameObject Box(string name, Vector3 pos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            Paint(go, color);
            return go;
        }

        static void Paint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.material.color = c;   // Built-in/URP 모두 MainColor 속성에 들어감
        }

        static Color FactionColor(string faction) =>
            faction == "east" ? new Color(0.7f, 0.35f, 0.25f) :
            faction == "west" ? new Color(0.25f, 0.4f, 0.7f) :
            new Color(0.4f, 0.45f, 0.3f);

        static Color ParseColor(string hex, Color fallback) =>
            !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
    }
}
