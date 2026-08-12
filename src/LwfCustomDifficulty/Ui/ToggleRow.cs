using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LwfCustomDifficulty.Ui
{
    /// <summary>A label and a button that flips a boolean.</summary>
    internal static class ToggleRow
    {
        internal static GameObject Create(Transform parent, string label, TMP_FontAsset font,
                                          Func<bool> read, Action<bool> write)
        {
            var root = CycleRow.BeginRow(parent, label + "Row");
            CycleRow.SetWidth(CycleRow.AddLabelCell(root.transform, label, font),
                              CycleRow.LabelWidth, flexible: 1f);

            var buttonGo = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(root.transform, worldPositionStays: false);

            var button = buttonGo.GetComponent<Button>();

            // The arrow buttons' sprite, as on the cycle row: both cells are pressed.
            CycleRow.ApplyCellStates(button, buttonGo.GetComponent<Image>(), GameSkin.Button);

            CycleRow.SetWidth(buttonGo, CycleRow.ValueWidth, flexible: 0f);

            var valueText = CycleRow.AddLabel(buttonGo.transform, Caption(read()), font,
                                              TextAlignmentOptions.Center,
                                              CycleRow.CellPadX, CycleRow.CellPadY);
            valueText.gameObject.name = CycleRow.ValueTextName;
            GameSkin.ApplyValueStyle(valueText, tint: false);
            valueText.alignment = CycleRow.RowAlignment;

            button.onClick.AddListener(() =>
            {
                var next = !read();
                write(next);

                // Read back rather than echo `next`: the write goes through the config's
                // normalisation, and the caption should show what was actually stored.
                valueText.text = Caption(read());
            });

            return root;
        }

        private static string Caption(bool value)
        {
            return value ? "on" : "off";
        }
    }
}
