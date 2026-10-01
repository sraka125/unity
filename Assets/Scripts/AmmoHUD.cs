using UnityEngine;
using UnityEngine.UI;

public class AmmoHUD : MonoBehaviour
{
    [SerializeField] private SimpleShoot gun;
    [SerializeField] private Text ammoText;
    [SerializeField] private bool createUiIfMissing = true;

    void Start()
    {
        if (!gun)
            gun = FindFirstObjectByType<SimpleShoot>();

        if (!ammoText && createUiIfMissing)
            ammoText = CreateDefaultText();

        if (gun)
        {
            gun.OnAmmoChanged += Refresh;
            Refresh();
        }
    }

    void OnDestroy()
    {
        if (gun)
            gun.OnAmmoChanged -= Refresh;
    }

    void Refresh()
    {
        if (!ammoText || !gun) { return; }

        ammoText.text = $"{gun.CurrentAmmo} / {gun.MagazineCapacity}   |   Reserve {gun.ReserveAmmo}";
    }

    Text CreateDefaultText()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (!canvas)
        {
            GameObject canvasGo = new GameObject("AmmoCanvas");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.AddComponent<GraphicRaycaster>();
        }

        GameObject textGo = new GameObject("AmmoText");
        textGo.transform.SetParent(canvas.transform, false);

        Text text = textGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (!text.font)
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 28;
        text.alignment = TextAnchor.LowerRight;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-24f, 24f);
        rect.sizeDelta = new Vector2(420f, 48f);

        return text;
    }
}
