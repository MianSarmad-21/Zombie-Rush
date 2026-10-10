using UnityEngine;

namespace ZombieRush.UI
{
    /// Keeps HUD and touch controls inside the phone's safe area (notches,
    /// rounded corners, gesture bars). Re-applies if the screen rotates/resizes.
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        RectTransform rect;
        Rect lastSafeArea;
        Vector2Int lastScreen;

        void Awake()
        {
            rect = (RectTransform)transform;
            Apply();
        }

        void Update()
        {
            if (Screen.safeArea != lastSafeArea || Screen.width != lastScreen.x || Screen.height != lastScreen.y)
                Apply();
        }

        void Apply()
        {
            lastSafeArea = Screen.safeArea;
            lastScreen = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;

            Vector2 min = lastSafeArea.position;
            Vector2 max = lastSafeArea.position + lastSafeArea.size;
            min.x /= Screen.width; min.y /= Screen.height;
            max.x /= Screen.width; max.y /= Screen.height;

            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
