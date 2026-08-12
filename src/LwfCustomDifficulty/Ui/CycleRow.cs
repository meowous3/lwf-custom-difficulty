using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LwfCustomDifficulty.Ui
{
    /// <summary>
    /// A label and a button carrying the current value; clicking advances to the next one.
    ///
    /// It also holds the metrics and the two primitives every row type shares. Nothing is
    /// laid out implicitly: the first attempt parented an unsized row straight onto the
    /// difficulty card and the label wrapped mid-word, so each row states its own height
    /// and each cell its own width.
    /// </summary>
    internal static class CycleRow
    {
        /// <summary>Canvas reference units, sized against the options panel's 560 width
        /// less its 20 of padding each side: 260 + 12 + 230 = 502, and the label takes
        /// the remaining 18 through its flexible width. 230 is what "Multiplicative"
        /// needs at this font size.</summary>
        internal const float RowHeight = 60f;
        internal const float LabelWidth = 260f;
        internal const float ValueWidth = 230f;
        internal const float FontSize = 28f;

        private const float CellSpacing = 12f;

        /// <summary>How far a cell's text sits inside that cell.
        ///
        /// The game expresses this as a negative sizeDelta on a stretched rect and never as a
        /// TMP margin: of the 468 texts in this scene that sit on an Image, 464 have a margin
        /// of exactly zero. The card's own two centred readouts inset by 10 on all four
        /// sides, so 10 is taken verbatim; the vertical drops to 5 because these rows are 60
        /// tall where the card's cells are 100, and a RectMask2D clips whatever overflows.</summary>
        internal const float CellPadX = 10f;
        internal const float CellPadY = 5f;

        /// <summary>Marks the one text per row that follows the card's header face rather
        /// than its title face. <see cref="CustomOptionsPanel"/>'s font pass reads it.</summary>
        internal const string HeaderTextName = "HeaderText";

        /// <summary>The resting colour of a value cell that has no sprite behind it.</summary>
        private static readonly Color CellColor = new Color(0f, 0f, 0f, 0.35f);

        /// <summary>
        /// Gives a Selectable built in code the interaction states the editor would have
        /// supplied, and makes them actually visible.
        ///
        /// <c>targetGraphic</c> is assigned by <c>Selectable.Reset()</c>, which is
        /// editor-only and never runs on a GameObject constructed at runtime — so these rows
        /// had none, and <c>DoStateTransition</c> had nothing to act on at all.
        ///
        /// Assigning it is only half of it, because ColorTint <em>multiplies</em>: it calls
        /// <c>CrossFadeColor</c>, which writes the CanvasRenderer colour, and what renders is
        /// the graphic's colour times that. What the other half has to be depends entirely on
        /// whether the cell has art behind it.
        ///
        /// A non-null <paramref name="sprite"/> is the ordinary path. The cell carries the
        /// card's own sprite at full white, so the multiply has real RGB to act on, and the
        /// card's own ColorBlock — lifted off a real arrow button — then means what it says:
        /// normal leaves the art alone, highlighted washes it warm, pressed greys it.
        ///
        /// Without a sprite the cell is a flat black fill, and the default ColorBlock differs
        /// from Normal only in RGB — 245 highlighted, 200 pressed, alpha 255 throughout — so
        /// against RGB 0 every state multiplies out to exactly the same pixels and the row
        /// stays as dead as it looks. That case keeps the older arrangement, where the
        /// graphic is white and each ColorBlock entry is literally the colour it renders.
        /// </summary>
        internal static void ApplyCellStates(Selectable selectable, Image image, Sprite sprite)
        {
            selectable.targetGraphic = image;

            if (GameSkin.Apply(image, sprite))
            {
                selectable.colors = GameSkin.Colors;
                return;
            }

            image.color = Color.white;

            var colors = selectable.colors;
            colors.normalColor = CellColor;
            colors.highlightedColor = new Color(0.30f, 0.30f, 0.30f, 0.55f);
            colors.pressedColor = new Color(0.55f, 0.55f, 0.55f, 0.75f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0f, 0f, 0f, 0.20f);
            // DoStateTransition multiplies this into every state. It reads 1 only because
            // ColorBlock.defaultColorBlock is a field initializer that runs on AddComponent;
            // a 0 here would render the whole value column transparent.
            colors.colorMultiplier = 1f;
            selectable.colors = colors;
        }

        internal static GameObject Create(Transform parent, string label, TMP_FontAsset font,
                                          Func<string> read, Action advance)
        {
            var root = BeginRow(parent, label + "Row");
            SetWidth(AddLabelCell(root.transform, label, font), LabelWidth, flexible: 1f);

            var buttonGo = new GameObject("Next", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(root.transform, worldPositionStays: false);

            var button = buttonGo.GetComponent<Button>();

            // The arrow buttons' sprite, because this cell is pressed rather than typed into.
            ApplyCellStates(button, buttonGo.GetComponent<Image>(), GameSkin.Button);

            SetWidth(buttonGo, ValueWidth, flexible: 0f);

            // The value is the button's own caption rather than a separate cell: at this
            // font size the longest GrowthMode name needs the whole value column, and it
            // leaves the row reading label-then-value like every other row.
            //
            // It is also the one caption in the panel whose length varies, and the padding
            // now takes 20 of the column it was already filling, so it shrinks to fit rather
            // than spilling over the sprite's border. That is the card's own answer to the
            // same problem: all three of its readouts autosize, at 16 to 36.
            var valueText = AddLabel(buttonGo.transform, read(), font, TextAlignmentOptions.Center,
                                     CellPadX, CellPadY);
            valueText.enableAutoSizing = true;
            valueText.fontSizeMax = FontSize;
            valueText.fontSizeMin = 18f;

            button.onClick.AddListener(() =>
            {
                advance();
                valueText.text = read();
            });

            return root;
        }

        /// <summary>The row container: one horizontal strip of fixed height inside the
        /// panel's vertical layout.</summary>
        internal static GameObject BeginRow(Transform parent, string name)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup),
                                      typeof(LayoutElement));
            root.transform.SetParent(parent, worldPositionStays: false);

            var layout = root.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = CellSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            var element = root.GetComponent<LayoutElement>();
            element.minHeight = RowHeight;
            element.preferredHeight = RowHeight;
            element.flexibleHeight = 0f;

            return root;
        }

        internal static void SetWidth(GameObject go, float preferred, float flexible)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            element.preferredWidth = preferred;
            element.flexibleWidth = flexible;
        }

        /// <summary>
        /// A row's leading label, drawn the way the card draws its own row headers: the
        /// header frame behind it, and that header's colour, weight and alignment on the text.
        ///
        /// The frame is a cell of its own rather than styling on the text, because that is the
        /// card's structure — `Headers/ImageRepaymentFrame` wrapping `TMPRepaymentHeader`,
        /// mirroring `Bodies/ImageRepaymentCount` wrapping `TMPRepaymentCount`. Matching it
        /// puts this panel's two columns on the same footing as the card's.
        ///
        /// Falls back to a bare transparent cell if the frame did not resolve, which leaves
        /// the arrangement the panel had before it borrowed any art.
        /// </summary>
        internal static GameObject AddLabelCell(Transform parent, string label, TMP_FontAsset font)
        {
            var cellGo = new GameObject(label + "Label", typeof(RectTransform), typeof(Image));
            cellGo.transform.SetParent(parent, worldPositionStays: false);

            var frame = cellGo.GetComponent<Image>();
            if (!GameSkin.Apply(frame, GameSkin.LabelFrame))
            {
                // Transparent rather than tinted: an unresolved frame should read as absent,
                // not as a second slab competing with the value cell beside it.
                frame.color = new Color(0f, 0f, 0f, 0f);
            }

            var text = AddLabel(cellGo.transform, label, font, TextAlignmentOptions.Center,
                                CellPadX, CellPadY);

            // Named so the panel's font pass can tell it apart. A header is set in the card's
            // header face, which is not the card's title face that every other text here uses.
            text.gameObject.name = HeaderTextName;

            if (!GameSkin.ApplyLabelStyle(text))
            {
                text.alignment = TextAlignmentOptions.MidlineLeft;
            }

            // The card's headers wrap to two lines; these are one line in a 260 column, so the
            // longest of them shrinks to fit rather than overrunning the frame. That is the
            // card's own answer for its readouts, which autosize between 16 and 36.
            text.enableAutoSizing = true;
            text.fontSizeMax = FontSize;
            text.fontSizeMin = 16f;

            return cellGo;
        }

        internal static TextMeshProUGUI AddLabel(Transform parent, string text, TMP_FontAsset font,
                                                 TextAlignmentOptions alignment,
                                                 float padX = 0f, float padY = 0f)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, worldPositionStays: false);

            // Stretching matters for the captions parented to a bare button, which no
            // layout group drives; where one does, it overwrites these anchors itself.
            Stretch(go.GetComponent<RectTransform>(), padX, padY);

            var component = go.GetComponent<TextMeshProUGUI>();
            component.font = font;
            component.text = text;
            component.alignment = alignment;
            component.fontSize = FontSize;
            component.enableAutoSizing = false;

            // Two-word labels would otherwise break across lines inside the label column.
            component.textWrappingMode = TextWrappingModes.NoWrap;
            component.overflowMode = TextOverflowModes.Overflow;
            return component;
        }

        /// <summary>
        /// Fills the parent, held off its edges by <paramref name="padX"/> and
        /// <paramref name="padY"/>.
        ///
        /// This is how the game insets everything it draws on a background: anchors at the
        /// corners, position zero, and the padding carried as a negative sizeDelta. Offsets
        /// are the same statement written the other way round, and unlike sizeDelta they do
        /// not need the parent's size to be known yet — which here it is not, since the
        /// layout groups size these cells after the fact.
        /// </summary>
        internal static void Stretch(RectTransform rect, float padX = 0f, float padY = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padX, padY);
            rect.offsetMax = new Vector2(-padX, -padY);
        }
    }
}
