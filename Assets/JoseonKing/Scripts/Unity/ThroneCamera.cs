using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace JoseonKing.UnityLayer
{
    // 용상에 앉은 임금의 눈. 오른쪽 마우스를 누른 채 움직이면 고개를 돌린다.
    // 왼쪽 클릭으로 대신을 지목한다.
    public class ThroneCamera : MonoBehaviour
    {
        public float yawLimit = 75f;
        public float pitchUp = 25f;
        public float pitchDown = 35f;
        public float sensitivity = 2.2f;

        public event Action<MinisterActor> OnMinisterClicked;

        float yaw, pitch;
        Quaternion baseRotation;
        Camera cam;

        void Start()
        {
            cam = GetComponent<Camera>();
            baseRotation = transform.rotation;
        }

        void Update()
        {
            if (InputCompat.RightMouseHeld)
            {
                var d = InputCompat.MouseDelta;
                yaw = Mathf.Clamp(yaw + d.x * sensitivity, -yawLimit, yawLimit);
                pitch = Mathf.Clamp(pitch - d.y * sensitivity, -pitchUp, pitchDown);
                transform.rotation = baseRotation * Quaternion.Euler(pitch, yaw, 0f);
            }

            if (InputCompat.LeftClickDown && !PointerOverUI())
            {
                var ray = cam.ScreenPointToRay(InputCompat.MousePosition);
                if (Physics.Raycast(ray, out var hit, 50f))
                {
                    var actor = hit.collider.GetComponentInParent<MinisterActor>();
                    if (actor != null) OnMinisterClicked?.Invoke(actor);
                }
            }
        }

        static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
