using JoseonKing.Core;
using UnityEngine;

namespace JoseonKing.UnityLayer
{
    // 대신 한 명. 회색 박스 캡슐이든 AI로 뽑은 프리팹이든 같은 방식으로 움직인다.
    // 프리팹에 Animator가 있으면 트리거 Talk / Idle / Bow 를 부른다.
    public class MinisterActor : MonoBehaviour
    {
        public MinisterDef Def { get; private set; }

        Animator animator;
        Renderer[] renderers;
        Light speakLight;
        TextMesh nameLabel;
        float speakUntil;

        static readonly int TalkHash = Animator.StringToHash("Talk");
        static readonly int IdleHash = Animator.StringToHash("Idle");
        static readonly int BowHash = Animator.StringToHash("Bow");

        public void Init(MinisterDef def, Font font, Color color, float labelHeight)
        {
            Def = def;
            animator = GetComponentInChildren<Animator>();
            renderers = GetComponentsInChildren<Renderer>();

            // 말할 때 켜지는 조명
            var lightGo = new GameObject("SpeakLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0, labelHeight + 0.4f, 0.6f);
            speakLight = lightGo.AddComponent<Light>();
            speakLight.type = LightType.Point;
            speakLight.color = new Color(1f, 0.85f, 0.55f);
            speakLight.range = 3.5f;
            speakLight.intensity = 0f;

            // 머리 위 이름표
            var labelGo = new GameObject("NameLabel");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = new Vector3(0, labelHeight, 0);
            nameLabel = labelGo.AddComponent<TextMesh>();
            nameLabel.text = def.name;
            nameLabel.anchor = TextAnchor.MiddleCenter;
            nameLabel.alignment = TextAlignment.Center;
            nameLabel.characterSize = 0.03f;   // 크면 앞뒤 줄 이름표가 겹친다
            nameLabel.fontSize = 48;
            nameLabel.color = Color.white;
            if (font != null)
            {
                nameLabel.font = font;
                labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }

            // 클릭으로 지목할 수 있게 콜라이더 보장
            if (GetComponentInChildren<Collider>() == null)
            {
                var col = gameObject.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0, 0.9f, 0);
                col.height = 1.8f;
                col.radius = 0.35f;
            }
        }

        public void Speak(float seconds = 3.5f)
        {
            speakUntil = Time.time + seconds;
            Trigger(TalkHash);
        }

        public void Bow() => Trigger(BowHash);

        // 프리팹 Animator에 그 트리거가 있을 때만 부른다 (없으면 경고 없이 넘어감)
        void Trigger(int hash)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (var p in animator.parameters)
                if (p.nameHash == hash && p.type == AnimatorControllerParameterType.Trigger)
                {
                    animator.SetTrigger(hash);
                    return;
                }
        }

        public void SetSelected(bool selected)
        {
            if (nameLabel != null) nameLabel.color = selected ? new Color(1f, 0.85f, 0.3f) : Color.white;
        }

        void Update()
        {
            bool speaking = Time.time < speakUntil;
            if (speakLight != null)
                speakLight.intensity = Mathf.MoveTowards(speakLight.intensity, speaking ? 2.5f : 0f, Time.deltaTime * 8f);

            if (!speaking && speakUntil > 0f)
            {
                Trigger(IdleHash);
                speakUntil = 0f;
            }

            // 이름표는 늘 카메라를 본다
            if (nameLabel != null && Camera.main != null)
                nameLabel.transform.rotation = Quaternion.LookRotation(nameLabel.transform.position - Camera.main.transform.position);
        }
    }
}
