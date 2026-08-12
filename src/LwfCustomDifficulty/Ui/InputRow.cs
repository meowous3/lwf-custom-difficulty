using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LwfCustomDifficulty.Ui
{
    /// <summary>A label and a typed number.</summary>
    internal static class InputRow
    {
        /// <summary>Nine digits reach the normaliser's cap of int.MaxValue / 4; the tenth
        /// character is there for a leading minus sign. The parse clamps on its own, so this
        /// is a second line of defence rather than the guard itself.</summary>
        private const int WholeNumberCharacterLimit = 10;

        /// <summary>
        /// <paramref name="read"/> renders the stored value, <paramref name="write"/> takes
        /// what was typed. The row re-reads after every commit, so what stays on screen is
        /// what the config actually holds — typing -5 into a field with a floor of 0 leaves
        /// 0 showing, not -5.
        /// </summary>
        internal static GameObject Create(Transform parent, string label, TMP_FontAsset font,
                                          Func<string> read, Action<string> write,
                                          bool decimals = false)
        {
            var root = CycleRow.BeginRow(parent, label + "Row");
            CycleRow.SetWidth(CycleRow.AddLabelCell(root.transform, label, font),
                              CycleRow.LabelWidth, flexible: 1f);

            // Built inactive on purpose. TMP_InputField creates its caret and selection
            // highlight only in OnEnable, and only when textComponent is already assigned;
            // adding the component to a live GameObject runs OnEnable immediately, before
            // this method can wire the text up, and the field is then left with no caret for
            // as long as nothing happens to disable and re-enable it. Deferring the first
            // enable until the wiring is done removes that dependency entirely.
            var fieldGo = new GameObject("Field", typeof(RectTransform));
            fieldGo.SetActive(false);
            fieldGo.transform.SetParent(root.transform, worldPositionStays: false);
            var background = fieldGo.AddComponent<Image>();
            CycleRow.SetWidth(fieldGo, CycleRow.ValueWidth, flexible: 0f);

            // The padding goes on the viewport rather than on the text, so the mask and the
            // caret respect it too: a caret at the end of a full field then stops short of
            // the cell's border instead of being clipped in half by it.
            var viewportGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(fieldGo.transform, worldPositionStays: false);
            CycleRow.Stretch(viewportGo.GetComponent<RectTransform>(),
                             CycleRow.CellPadX, CycleRow.CellPadY);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(viewportGo.transform, worldPositionStays: false);
            CycleRow.Stretch(textGo.GetComponent<RectTransform>());
            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.font = font;

            // Centred, not right-aligned. Right-aligned text pinned the digit flat against
            // the cell's edge with nowhere to go, and right alignment was not the game's
            // habit anyway: of the 468 texts in this scene that sit on an Image, 312 are
            // centred and 156 are left-aligned, and not one of them is right-aligned. The
            // card's own two number cells are centred, and these now read the same way.
            textGo.name = CycleRow.ValueTextName;

            GameSkin.ApplyValueStyle(text);
            text.alignment = CycleRow.RowAlignment;

            text.fontSize = CycleRow.FontSize;
            text.enableAutoSizing = false;

            var field = fieldGo.AddComponent<TMP_InputField>();
            field.textViewport = viewportGo.GetComponent<RectTransform>();
            field.textComponent = text;
            field.contentType = decimals
                ? TMP_InputField.ContentType.DecimalNumber
                : TMP_InputField.ContentType.IntegerNumber;
            field.pointSize = CycleRow.FontSize;
            if (!decimals) field.characterLimit = WholeNumberCharacterLimit;

            // TMP_InputField is a Selectable too, built the same way, and had the same dead
            // states as the two button rows. Applied while the object is still inactive,
            // which DoStateTransition ignores; the SetActive below runs it for real.
            //
            // The value-cell sprite rather than the button one: this cell is typed into, and
            // the card dresses its own numbers in exactly this sprite.
            CycleRow.ApplyCellStates(field, background, GameSkin.ValueCell);

            fieldGo.SetActive(true);

            field.text = read();
            field.onEndEdit.AddListener(typed =>
            {
                write(typed);

                // Without notify: the field is the thing being written to, and re-entering
                // the change callbacks from inside one of them buys nothing.
                field.SetTextWithoutNotify(read());
            });

            return root;
        }
    }
}
