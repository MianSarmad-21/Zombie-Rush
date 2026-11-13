using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ZombieRush.UI.Kit
{
    /// The magazine as a row of cartridges: one per round it holds, live rounds bright, spent ones dark
    /// husks. A big magazine packs its cartridges tighter so the row keeps its width. The newest shot pops.
    public class MagazineView : MonoBehaviour
    {
        public Sprite bullet;
        [Tooltip("Width the whole row may take, canvas units.")]
        public float rowWidth = 300f;
        public float bulletHeight = 34f;
        public float maxSpacing = 18f;
        public Color live = Color.white;
        public Color spent = new Color(0.15f, 0.15f, 0.13f, 0.7f);

        readonly List<Image> pool = new List<Image>();
        int shown = -1, size = -1;
        float popAt = -1f;
        int popIndex = -1;

        public void Set(int rounds, int magazineSize)
        {
            magazineSize = Mathf.Clamp(magazineSize, 0, 60);
            if (magazineSize != size) Build(magazineSize);
            if (shown >= 0 && rounds < shown) { popIndex = rounds; popAt = Time.unscaledTime; }
            shown = rounds;
            for (int i = 0; i < pool.Count; i++)
            {
                pool[i].gameObject.SetActive(i < size);
                pool[i].color = i < rounds ? live : spent;
                pool[i].rectTransform.localScale = Vector3.one;
            }
        }

        void Build(int count)
        {
            size = count;
            float spacing = count > 1 ? Mathf.Min(maxSpacing, rowWidth / count) : maxSpacing;
            float w = Mathf.Min(spacing * 0.78f, bulletHeight * 0.4f);
            while (pool.Count < count)
            {
                var go = new GameObject("Round", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                var img = go.GetComponent<Image>();
                img.sprite = bullet; img.raycastTarget = false; img.preserveAspect = false;
                pool.Add(img);
            }
            for (int i = 0; i < pool.Count; i++)
            {
                var rt = pool[i].rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(w, bulletHeight);
                rt.anchoredPosition = new Vector2(-(i + 0.5f) * spacing, 0f);   // rounds fire from the right end
            }
        }

        void Update()
        {
            if (popIndex < 0 || popIndex >= pool.Count) return;
            float t = (Time.unscaledTime - popAt) / 0.18f;
            var rt = pool[popIndex].rectTransform;
            if (t >= 1f) { rt.localScale = Vector3.one; popIndex = -1; return; }
            rt.localScale = new Vector3(1f, 1f + 0.5f * (1f - t), 1f);
        }
    }
}
