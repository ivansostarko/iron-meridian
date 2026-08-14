using UnityEngine;
using UnityEngine.SceneManagement;
using IronMeridian.Core;

namespace IronMeridian.UI
{
    /// <summary>Placeholder page for the East France scenario map.</summary>
    public class EastFranceUI : MonoBehaviour
    {
        void Start()
        {
            IronMeridian.Audio.AudioManager.Apply();
            IronMeridian.Audio.MusicManager.Play(IronMeridian.Audio.MusicTrack.MenuTheme);
            var canvas = UIFactory.CreateCanvas("EastFranceCanvas");

            UIFactory.CreateScreenBackground(canvas.transform, BackgroundId.Default);

            var title = UIFactory.CreateText(canvas.transform, "MAP — EAST FRANCE", 48,
                GameConfig.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -120), new Vector2(1200, 80));

            var msg = UIFactory.CreateText(canvas.transform, "UNDER DEVELOPMENT", 72,
                GameConfig.UiAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(msg.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400, 120));

            var sub = UIFactory.CreateText(canvas.transform,
                "The Eastern France operational theatre will be available in a future build.",
                26, GameConfig.UiTextDim);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(1200, 60));

            UIFactory.CreateBackButton(canvas.transform, "BACK TO DEVELOPMENT",
                () => SceneManager.LoadScene(GameConfig.SceneTesting),
                new Vector2(0.5f, 0f), new Vector2(0, 90), new Vector2(360, 66));
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                SceneManager.LoadScene(GameConfig.SceneTesting);
        }
    }
}
