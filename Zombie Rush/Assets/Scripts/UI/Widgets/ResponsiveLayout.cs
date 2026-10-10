using System;
using UnityEngine;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    [Serializable]
    public struct RectPose
    {
        public Vector2 anchorMin, anchorMax, pivot, anchoredPosition;

        public static RectPose Of(RectTransform rt) => new RectPose
        {
            anchorMin = rt.anchorMin, anchorMax = rt.anchorMax, pivot = rt.pivot, anchoredPosition = rt.anchoredPosition,
        };

        public void ApplyTo(RectTransform rt)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
        }
    }

    /// Moves HUD elements between their PC and touch positions (touch buttons
    /// occupy the bottom corners, so health/ammo move up) and toggles
    /// scheme-specific objects. Re-applies when the Controls option changes.
    public class ResponsiveLayout : MonoBehaviour
    {
        public RectTransform[] targets = Array.Empty<RectTransform>();
        public RectPose[] pcPoses = Array.Empty<RectPose>();
        public RectPose[] touchPoses = Array.Empty<RectPose>();
        public GameObject[] pcOnly = Array.Empty<GameObject>();
        public GameObject[] touchOnly = Array.Empty<GameObject>();

        void OnEnable()
        {
            GameSettings.Changed += Apply;
            Apply();
        }

        void OnDisable() => GameSettings.Changed -= Apply;

        public void Apply()
        {
            bool touch = GameSettings.UseTouchControls;
            var poses = touch ? touchPoses : pcPoses;
            for (int i = 0; i < targets.Length && i < poses.Length; i++)
                if (targets[i] != null) poses[i].ApplyTo(targets[i]);

            foreach (var go in pcOnly) if (go != null && go.activeSelf == touch) go.SetActive(!touch);
            foreach (var go in touchOnly) if (go != null && go.activeSelf != touch) go.SetActive(touch);
        }
    }
}
