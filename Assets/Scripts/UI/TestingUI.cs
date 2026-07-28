using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Core;

namespace IronMeridian.UI
{
    /// <summary>
    /// Testing hub. Two cards:
    ///   DEV             -> loads the main game screen (Lyon dev map)
    ///   MAP EAST FRANCE -> placeholder page ("Under development")
    /// </summary>
    public class TestingUI : MonoBehaviour
    {
        void Start()
        {
            IronMeridian.Audio.AudioManager.Apply();
            var canvas = UIFactory.CreateCanvas("TestingCanvas");

            var bg = UIFactory.CreatePanel(canvas.transform, "Background", GameConfig.UiBackground);
            UIFactory.Stretch(bg);

            var title = UIFactory.CreateText(canvas.transform, "TESTING", 56,
                GameConfig.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(80, -70), new Vector2(600, 80));

            var back = UIFactory.CreateButton(canvas.transform, "< BACK",
                () => SceneManager.LoadScene(GameConfig.SceneMainMenu),
                GameConfig.UiPanelLight, GameConfig.UiText, 24);
            UIFactory.Place((RectTransform)back.transform, new Vector2(1f, 1f), new Vector2(-80, -70), new Vector2(180, 60));

            Card(canvas.transform, -330, "DEV",
                "Main game screen.\nCesium 3D terrain over Lyon.\nDeploy units, draw lines, fight.",
                new Color(0.13f, 0.24f, 0.38f),
                () => SceneManager.LoadScene(GameConfig.SceneGame));

            Card(canvas.transform, 330, "MAP EAST FRANCE",
                "Eastern France scenario map.\nOperational theatre from\nLyon to the Rhine.",
                new Color(0.28f, 0.16f, 0.13f),
                () => SceneManager.LoadScene(GameConfig.SceneEastFrance));
        }

        void Card(Transform parent, float x, string title, string body, Color tone,
            UnityEngine.Events.UnityAction onClick)
        {
            var card = UIFactory.CreateButton(parent, "", onClick, tone, GameConfig.UiText);
            var rt = (RectTransform)card.transform;
            UIFactory.Place(rt, new Vector2(0.5f, 0.5f), new Vector2(x, -20), new Vector2(560, 420));

            // Remove the default centered label created by CreateButton
            foreach (Transform child in rt) if (child.name == "Text") Destroy(child.gameObject);

            var head = UIFactory.CreatePanel(rt, "Head", new Color(0, 0, 0, 0.35f));
            head.anchorMin = new Vector2(0, 1); head.anchorMax = new Vector2(1, 1);
            head.pivot = new Vector2(0.5f, 1);
            head.offsetMin = new Vector2(0, -110);
            head.offsetMax = Vector2.zero;

            var t = UIFactory.CreateText(head, title, 40, GameConfig.UiText,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(t.rectTransform);

            var b = UIFactory.CreateText(rt, body, 26, GameConfig.UiTextDim);
            UIFactory.Place(b.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(500, 220));

            var strip = UIFactory.CreatePanel(rt, "Strip", GameConfig.UiAccent);
            strip.anchorMin = Vector2.zero; strip.anchorMax = new Vector2(1, 0);
            strip.pivot = new Vector2(0.5f, 0);
            strip.offsetMin = Vector2.zero;
            strip.offsetMax = new Vector2(0, 8);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                SceneManager.LoadScene(GameConfig.SceneMainMenu);
        }
    }
}
