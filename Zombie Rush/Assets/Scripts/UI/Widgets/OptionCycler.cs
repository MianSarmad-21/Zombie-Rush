using System;
using UnityEngine;
using UnityEngine.UI;

namespace ZombieRush.UI
{
    /// "<  VALUE  >" picker. Big arrow buttons work equally well with a mouse,
    /// a keyboard and a thumb, unlike a dropdown.
    public class OptionCycler : MonoBehaviour
    {
        public Button prevButton;
        public Button nextButton;
        public Text valueText;

        public event Action<int> Changed;
        public int Index { get; private set; }

        string[] options = Array.Empty<string>();

        void Awake()
        {
            if (prevButton != null) prevButton.onClick.AddListener(() => Step(-1));
            if (nextButton != null) nextButton.onClick.AddListener(() => Step(1));
        }

        public void SetOptions(string[] values, int index)
        {
            options = values ?? Array.Empty<string>();
            Index = options.Length == 0 ? 0 : Mathf.Clamp(index, 0, options.Length - 1);
            Refresh();
        }

        public void Step(int direction)
        {
            if (options.Length == 0) return;
            Index = (Index + direction + options.Length) % options.Length;
            Refresh();
            Changed?.Invoke(Index);
        }

        void Refresh()
        {
            if (valueText != null) valueText.text = options.Length > 0 ? options[Index] : "-";
        }
    }
}
