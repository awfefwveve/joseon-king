// 화면 UI를 코드로 만든다 (씬에 아무것도 배치할 필요 없음).
// 원칙: 화면에 게임 수치를 띄우지 않는다. 날짜와 신하들의 말, 소문, 보고만 보인다.
// (개발용으로 F1을 누르면 실제 수치를 볼 수 있다)
// 프로토타입이라 uGUI 기본 Text를 쓴다. 정식 버전에서 TextMeshPro + 한글 폰트 에셋으로 바꾼다.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JoseonKing.UnityLayer
{
    public class CourtUI
    {
        static readonly Color PanelBg = new Color(0.08f, 0.06f, 0.05f, 0.86f);
        public static readonly Color Gold = new Color(0.93f, 0.8f, 0.5f);
        public static readonly Color Ink = new Color(0.95f, 0.92f, 0.86f);
        public static readonly Color Quiet = new Color(0.68f, 0.66f, 0.62f);
        public static readonly Color KingColor = new Color(1f, 0.9f, 0.6f);
        static readonly Color ButtonBg = new Color(0.45f, 0.16f, 0.12f, 1f);
        static readonly Color ButtonAlt = new Color(0.22f, 0.2f, 0.18f, 1f);

        readonly Font font;
        readonly RectTransform canvasRt;

        Text dateText, debugText, logText;
        readonly List<string> logLines = new List<string>();
        Image healthVeil;

        // 아래 회의 막대
        GameObject councilBar;
        Text agendaText, addressedText;
        Button askButton, approveButton, rejectButton, commandButton, endCouncilButton;
        Text approveLabel, rejectLabel;

        // 가운데 창들
        GameObject commandPanel, eventPanel, eveningPanel, reportPanel, endingPanel;
        RectTransform commandList, eventChoices;
        Text eventTitle, eventBody, eventSource, reportText, endingText;
        readonly Dictionary<string, Button> eveningButtons = new Dictionary<string, Button>();

        public event Action OnAsk, OnApprove, OnReject, OnOpenCommands, OnEndCouncil, OnNextDay, OnRestart;
        public event Action<string> OnCommand;   // 하교할 어명 ID
        public event Action<int> OnChoice;
        public event Action<string> OnEvening;   // rest, educate, banquet, incognito

        public CourtUI(Font font)
        {
            this.font = font;

            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                InputCompat.AddUIInputModule(es);
            }

            var canvasGo = new GameObject("CourtCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasRt = canvasGo.GetComponent<RectTransform>();

            // 건강이 나쁘면 화면 가장자리가 어두워진다 (숫자 대신 몸으로 느끼게)
            var veil = Panel(canvasRt, "HealthVeil", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.05f, 0, 0, 0));
            healthVeil = veil.GetComponent<Image>();
            healthVeil.raycastTarget = false;

            BuildTopBar();
            BuildLog();
            BuildCouncilBar();
            BuildCommandPanel();
            BuildEventPanel();
            BuildEveningPanel();
            BuildReportPanel();
            BuildEndingPanel();
            HideAllPopups();
        }

        // ───────── 공개 메서드 ─────────

        public void SetDate(string text) => dateText.text = text;

        public void SetDebug(string text) => debugText.text = text;
        public void ToggleDebug() => debugText.gameObject.SetActive(!debugText.gameObject.activeSelf);

        // health 0~100. 50 아래부터 화면이 어두워진다
        public void SetHealthVeil(int health)
        {
            float a = health >= 50 ? 0f : Mathf.Lerp(0.45f, 0f, health / 50f);
            healthVeil.color = new Color(0.05f, 0f, 0f, a);
        }

        public void Log(string line, Color? color = null)
        {
            string c = ColorUtility.ToHtmlStringRGB(color ?? Ink);
            logLines.Add($"<color=#{c}>{line}</color>");
            while (logLines.Count > 16) logLines.RemoveAt(0);
            logText.text = string.Join("\n\n", logLines);
        }

        public void ClearLog() { logLines.Clear(); logText.text = ""; }

        public void SetAddressed(string name) =>
            addressedText.text = string.IsNullOrEmpty(name) ? "대신을 클릭하면 그 대신에게 따로 묻습니다" : "지목: " + name;

        // agenda가 null이면 안건 없음. raisedByKing이면 버튼 문구가 바뀐다
        public void SetAgenda(string agenda, bool raisedByKing, bool canDecide, bool busy)
        {
            bool has = agenda != null;
            agendaText.text = has ? agenda : "올라온 안건이 없사옵니다. 하교하시거나 회의를 파하소서.";
            approveLabel.text = raisedByKing ? "그리 시행하라" : "윤허한다";
            rejectLabel.text = !canDecide ? "다음에 논의하자" : raisedByKing ? "거두겠다" : "불허한다";
            askButton.interactable = has && !busy;
            approveButton.interactable = has && canDecide && !busy;
            rejectButton.interactable = has && !busy;
            commandButton.interactable = !busy && canDecide;
            endCouncilButton.interactable = !busy;
        }

        public void ShowCouncil()
        {
            HideAllPopups();
            councilBar.SetActive(true);
        }

        public void ShowCommands(IList<(string id, string text, bool ready)> commands)
        {
            foreach (Transform child in commandList) UnityEngine.Object.Destroy(child.gameObject);
            foreach (var c in commands)
            {
                string id = c.id;
                var b = MakeButton(commandList, c.ready ? c.text : c.text + "  (얼마 전 시행함)", () => OnCommand?.Invoke(id), c.ready ? ButtonBg : ButtonAlt, 22, TextAnchor.MiddleLeft);
                b.interactable = c.ready;
                b.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            }
            commandPanel.SetActive(true);
        }

        public void HideCommands() => commandPanel.SetActive(false);

        public void ShowEvent(string title, string body, string source, IList<string> choices)
        {
            HideAllPopups();
            councilBar.SetActive(false);
            eventTitle.text = title;
            eventBody.text = body;
            eventSource.text = string.IsNullOrEmpty(source) ? "" : "출전: " + source;
            foreach (Transform child in eventChoices) UnityEngine.Object.Destroy(child.gameObject);
            for (int i = 0; i < choices.Count; i++)
            {
                int idx = i;
                var b = MakeButton(eventChoices, choices[i], () => OnChoice?.Invoke(idx), ButtonBg);
                b.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
            }
            eventPanel.SetActive(true);
        }

        public void ShowEvening(bool canEducate)
        {
            HideAllPopups();
            councilBar.SetActive(false);
            eveningButtons["educate"].interactable = canEducate;
            eveningPanel.SetActive(true);
        }

        public void ShowReport(string text)
        {
            HideAllPopups();
            councilBar.SetActive(false);
            reportText.text = text;
            reportPanel.SetActive(true);
        }

        public void ShowEnding(string text)
        {
            HideAllPopups();
            councilBar.SetActive(false);
            endingText.text = text;
            endingPanel.SetActive(true);
        }

        void HideAllPopups()
        {
            commandPanel.SetActive(false);
            eventPanel.SetActive(false);
            eveningPanel.SetActive(false);
            reportPanel.SetActive(false);
            endingPanel.SetActive(false);
        }

        // ───────── 화면 구성 ─────────

        void BuildTopBar()
        {
            var bar = Panel(canvasRt, "TopBar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -64), Vector2.zero, PanelBg);
            dateText = MakeText(bar, "Date", 28, Gold, TextAnchor.MiddleLeft);
            Place(dateText.rectTransform, Vector2.zero, Vector2.one, new Vector2(24, 0), new Vector2(-24, 0));

            debugText = MakeText(canvasRt, "Debug", 18, new Color(0.6f, 1f, 0.6f), TextAnchor.UpperLeft);
            Place(debugText.rectTransform, new Vector2(0, 1), new Vector2(0.6f, 1), new Vector2(16, -200), new Vector2(0, -72));
            debugText.gameObject.SetActive(false);
        }

        void BuildLog()
        {
            var panel = Panel(canvasRt, "Log", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-660, 190), new Vector2(-16, -80), PanelBg);
            logText = MakeText(panel, "LogText", 23, Ink, TextAnchor.LowerLeft);
            logText.supportRichText = true;
            logText.verticalOverflow = VerticalWrapMode.Truncate;
            Place(logText.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 16), new Vector2(-20, -16));
        }

        void BuildCouncilBar()
        {
            var bar = Panel(canvasRt, "CouncilBar", new Vector2(0, 0), new Vector2(1, 0), new Vector2(16, 16), new Vector2(-16, 176), PanelBg);
            councilBar = bar.gameObject;

            agendaText = MakeText(bar, "Agenda", 26, Gold, TextAnchor.UpperLeft);
            Place(agendaText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -56), new Vector2(-20, -12));
            addressedText = MakeText(bar, "Addressed", 19, Quiet, TextAnchor.UpperRight);
            Place(addressedText.rectTransform, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(0, -88), new Vector2(-20, -60));

            askButton = MakeButton(bar, "경들의 생각은 어떠한가", () => OnAsk?.Invoke(), ButtonAlt);
            approveButton = MakeButton(bar, "윤허한다", () => OnApprove?.Invoke(), ButtonBg);
            rejectButton = MakeButton(bar, "불허한다", () => OnReject?.Invoke(), ButtonAlt);
            commandButton = MakeButton(bar, "하교하기", () => OnOpenCommands?.Invoke(), ButtonAlt);
            endCouncilButton = MakeButton(bar, "회의를 파한다", () => OnEndCouncil?.Invoke(), ButtonAlt);
            approveLabel = approveButton.GetComponentInChildren<Text>();
            rejectLabel = rejectButton.GetComponentInChildren<Text>();

            var buttons = new[] { askButton, approveButton, rejectButton, commandButton, endCouncilButton };
            float[] widths = { 0.28f, 0.16f, 0.16f, 0.18f, 0.18f };
            float x = 0.02f;
            for (int i = 0; i < buttons.Length; i++)
            {
                Place(buttons[i].GetComponent<RectTransform>(), new Vector2(x, 0), new Vector2(x + widths[i] - 0.01f, 0), new Vector2(0, 16), new Vector2(0, 80));
                x += widths[i];
            }
        }

        void BuildCommandPanel()
        {
            var p = Panel(canvasRt, "Commands", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-640, -380), new Vector2(180, 360), new Color(0.12f, 0.09f, 0.07f, 0.97f));
            commandPanel = p.gameObject;
            var title = MakeText(p, "Title", 30, Gold, TextAnchor.UpperLeft);
            title.text = "무엇을 하교하시겠습니까";
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -64), new Vector2(-28, -18));

            var list = new GameObject("List", typeof(RectTransform));
            list.transform.SetParent(p, false);
            commandList = list.GetComponent<RectTransform>();
            Place(commandList, Vector2.zero, Vector2.one, new Vector2(28, 90), new Vector2(-28, -76));
            var layout = list.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            var close = MakeButton(p, "그만두겠다", HideCommands, ButtonAlt);
            Place(close.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-140, 20), new Vector2(140, 76));
        }

        void BuildEventPanel()
        {
            var p = Panel(canvasRt, "Event", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-640, -340), new Vector2(180, 330), new Color(0.12f, 0.09f, 0.07f, 0.96f));
            eventPanel = p.gameObject;
            eventTitle = MakeText(p, "Title", 34, Gold, TextAnchor.UpperLeft);
            Place(eventTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -70), new Vector2(-28, -20));
            eventBody = MakeText(p, "Body", 25, Ink, TextAnchor.UpperLeft);
            Place(eventBody.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -260), new Vector2(-28, -80));
            eventSource = MakeText(p, "Source", 19, Quiet, TextAnchor.UpperLeft);
            Place(eventSource.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -296), new Vector2(-28, -262));

            var choices = new GameObject("Choices", typeof(RectTransform));
            choices.transform.SetParent(p, false);
            eventChoices = choices.GetComponent<RectTransform>();
            Place(eventChoices, new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 24), new Vector2(-28, 340));
            var layout = choices.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.LowerCenter;
        }

        void BuildEveningPanel()
        {
            var p = Panel(canvasRt, "Evening", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-540, -230), new Vector2(180, 230), new Color(0.06f, 0.07f, 0.12f, 0.96f));
            eveningPanel = p.gameObject;
            var title = MakeText(p, "Title", 32, Gold, TextAnchor.UpperCenter);
            title.text = "해가 저물었다. 침전에서 무엇을 하시겠습니까";
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -70), new Vector2(-20, -20));

            string[,] items =
            {
                { "rest", "일찍 침전에 든다" },
                { "educate", "세자를 불러 경서를 강론한다" },
                { "banquet", "대신들을 불러 술자리를 연다" },
                { "incognito", "평복으로 갈아입고 몰래 민가를 돌아본다" }
            };
            for (int i = 0; i < 4; i++)
            {
                string key = items[i, 0];
                var b = MakeButton(p, items[i, 1], () => OnEvening?.Invoke(key), i == 0 ? ButtonBg : ButtonAlt);
                float y0 = -100 - i * 84;
                Place(b.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, y0 - 70), new Vector2(-40, y0));
                eveningButtons[key] = b;
            }
        }

        void BuildReportPanel()
        {
            var p = Panel(canvasRt, "Report", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-540, -320), new Vector2(180, 320), new Color(0.1f, 0.09f, 0.07f, 0.96f));
            reportPanel = p.gameObject;
            reportText = MakeText(p, "ReportText", 25, Ink, TextAnchor.UpperLeft);
            reportText.supportRichText = true;
            Place(reportText.rectTransform, Vector2.zero, Vector2.one, new Vector2(28, 100), new Vector2(-28, -24));
            var next = MakeButton(p, "다음 상참으로", () => OnNextDay?.Invoke(), ButtonBg);
            Place(next.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-160, 20), new Vector2(160, 84));
        }

        void BuildEndingPanel()
        {
            var p = Panel(canvasRt, "Ending", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0.94f));
            endingPanel = p.gameObject;
            endingText = MakeText(p, "EndingText", 30, Gold, TextAnchor.MiddleCenter);
            Place(endingText.rectTransform, Vector2.zero, Vector2.one, new Vector2(240, 200), new Vector2(-240, -100));
            var again = MakeButton(p, "새 왕조로 다시 시작", () => OnRestart?.Invoke(), ButtonBg);
            Place(again.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-200, 90), new Vector2(200, 160));
        }

        // ───────── 만들기 도우미 ─────────

        static RectTransform Panel(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            var rt = go.GetComponent<RectTransform>();
            Place(rt, aMin, aMax, oMin, oMax);
            return rt;
        }

        static void Place(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = oMin;
            rt.offsetMax = oMax;
        }

        Text MakeText(Transform parent, string name, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        Button MakeButton(Transform parent, string label, UnityAction onClick, Color bg, int size = 24, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = bg;
            var b = go.GetComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.2f, 1.15f, 1.1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            b.colors = colors;
            b.onClick.AddListener(onClick);
            var t = MakeText(go.transform, "Label", size, Ink, align);
            t.text = label;
            Place(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 4), new Vector2(-16, -4));
            return b;
        }
    }
}
