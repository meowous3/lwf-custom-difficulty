using System.Reflection;
using TMPro;
using HarmonyLib;
using Scene.TitleScene;
using UnityEngine;
using UnityEngine.UI;

namespace LwfCustomDifficulty.Ui
{
    /// <summary>
    /// The panel's art, borrowed wholesale from the difficulty card.
    ///
    /// Nothing here is authored and nothing is loaded. Every sprite is a reference lifted off
    /// an <see cref="Image"/> that the scene already built, exactly as the font is lifted off
    /// <c>_difficultyText</c>. That matters beyond tidiness: these sprites are members of the
    /// <c>atlas_ui</c> SpriteAtlas and carry no standalone texture of their own, so there is
    /// no asset path and no bundle to load them from. Holding the live reference is the only
    /// way to get them.
    ///
    /// Which sprite goes where is not a preference. The card puts <c>bg_shortcut</c> behind
    /// each of its three numbers and <c>button_action</c> behind each of its two arrows, and
    /// this panel is eight rows of numbers plus two pressable cells, so it uses the same
    /// sprite for the same job.
    /// </summary>
    internal static class GameSkin
    {
        /// <summary>Children of the difficulty card. Resolving a child by name is the
        /// screen's own idiom — <c>DifficultySetter.ResolveClearedImage</c> finds its icon
        /// with <c>image.name == "ImageCleared"</c> — but it is still build-specific, so both
        /// are asserted at resolve time and the result is logged either way.</summary>
        private const string PanelSource = "ImageBGBody_1";       // frame_corner_big, border 37
        private const string ValueSource = "ImageRepaymentCount"; // bg_shortcut,      border 4

        /// <summary>The card's own row headers. Under `BodiesHeaders`, `Bodies` holds the
        /// three value cells and `Headers` the three labels beside them, each an
        /// `ImageRepaymentFrame`-style frame wrapping a `TMPRepaymentHeader`-style text.
        /// "Number of Repayments" is the first of those.</summary>
        private const string LabelFrameSource = "ImageRepaymentFrame";
        private const string LabelTextSource = "TMPRepaymentHeader";

        /// <summary>The text inside `Bodies/ImageRepaymentCount` — the card's own value
        /// readout, set in a different face and colour from its headers.</summary>
        private const string ValueTextSource = "TMPRepaymentCount";

        private static readonly FieldInfo LeftButtonField =
            AccessTools.Field(typeof(DifficultySetter), "_leftButton");

        /// <summary>The card's own body. A 9-sliced frame with 37 units of corner, which the
        /// 560x630 panel clears in both directions.</summary>
        internal static Sprite Panel { get; private set; }

        /// <summary>What the card puts behind a number. Its border is only 4 units, so it
        /// holds its shape at this panel's 230x60 as readily as at the card's 206x100.</summary>
        internal static Sprite ValueCell { get; private set; }

        /// <summary>The arrow buttons' background, for the two cells that are pressable.</summary>
        internal static Sprite Button { get; private set; }

        /// <summary>The frame the card draws behind a row header.</summary>
        internal static Sprite LabelFrame { get; private set; }

        /// <summary>
        /// A live header text, kept as a template rather than copied into constants.
        ///
        /// Colour, weight and alignment are read off it at the moment a label is built, so the
        /// panel's labels are the card's labels by construction. Its own colour never appears
        /// here as a literal: the scene's MonoBehaviour typetrees cannot be read without the
        /// script assembly, so a literal would have been eyedropped from a screenshot and
        /// would drift the first time the game restyled.
        /// </summary>
        internal static TMP_Text LabelStyle { get; private set; }

        /// <summary>A live value readout, kept as a template for the same reason as
        /// <see cref="LabelStyle"/>. The card sets its numbers apart from its headers, and
        /// this panel's two columns are the same two jobs.</summary>
        internal static TMP_Text ValueStyle { get; private set; }

        /// <summary>
        /// The card's own interaction colours, read off a real button rather than guessed.
        ///
        /// These are what make the states visible again. ColorTint multiplies the ColorBlock
        /// into the graphic's colour, and the old cells were a flat black fill — every state
        /// multiplied out to identical pixels, which is why <see cref="CycleRow.ApplyCellStates"/>
        /// had to abandon the ColorBlock's own meaning and carry the whole appearance itself.
        /// A sprite cell is white and carries its own RGB, so the multiply finally has
        /// something to act on and the game's values do what they say: normal leaves the art
        /// alone, highlighted washes it warm, pressed greys it down.
        /// </summary>
        internal static ColorBlock Colors { get; private set; } = ColorBlock.defaultColorBlock;

        /// <summary>True once every sprite above resolved.</summary>
        internal static bool Resolved { get; private set; }

        /// <summary>
        /// Resolves once and then does nothing, so this is safe to call from every
        /// SetDifficulty.
        ///
        /// The card has always finished <c>Initialize</c> by the time this runs — the first
        /// SetDifficulty is the last thing Initialize does — so every child is present.
        /// </summary>
        internal static void Resolve(DifficultySetter setter)
        {
            if (Resolved || setter == null) return;

            var images = setter.GetComponentsInChildren<Image>(includeInactive: true);

            Panel = Find(images, PanelSource);
            ValueCell = Find(images, ValueSource);
            LabelFrame = Find(images, LabelFrameSource);

            LabelStyle = FindText(setter, LabelTextSource);
            ValueStyle = FindText(setter, ValueTextSource);

            // A serialized field rather than a child name: it is the card's own reference to
            // the button, and it survives the GameObject being renamed.
            var left = LeftButtonField?.GetValue(setter) as Button;
            Button = left != null && left.image != null ? left.image.sprite : null;
            if (left != null) Colors = left.colors;

            Resolved = Panel != null && ValueCell != null && Button != null;

            // The resolved values, not the intent: a name that stopped matching shows up here
            // as a null instead of quietly reverting the panel to grey.
            Plugin.Log.LogInfo($"Skin: panel={Name(Panel)} value={Name(ValueCell)} "
                               + $"button={Name(Button)} labelFrame={Name(LabelFrame)} "
                               + $"labelFont={Face(LabelStyle)} labelColor={Tint(LabelStyle)} valueFont={Face(ValueStyle)} valueColor={Tint(ValueStyle)} "
                               + $"normal={Colors.normalColor} "
                               + $"highlighted={Colors.highlightedColor} pressed={Colors.pressedColor} "
                               + $"resolved={Resolved}");

            if (!Resolved)
            {
                Plugin.Log.LogWarning("Skin: incomplete; unresolved cells keep their untextured fill.");
            }
        }

        private static TMP_Text FindText(DifficultySetter setter, string name)
        {
            foreach (var text in setter.GetComponentsInChildren<TMP_Text>(includeInactive: true))
            {
                if (text != null && text.name == name) return text;
            }

            return null;
        }

        /// <summary>
        /// Draws a label the way the card draws a row header: its face, colour, weight and
        /// alignment — but not its size. These rows are 60 tall against the card's two-line
        /// headers, so the caller sizes the text itself.
        /// </summary>
        internal static bool ApplyLabelStyle(TMP_Text text)
        {
            return ApplyTextStyle(text, LabelStyle);
        }

        /// <summary>
        /// Draws a value the way the card draws one of its numbers. Size is the caller's,
        /// as above: these cells are 60 tall against the card's 100.
        ///
        /// <paramref name="tint"/> is false for a caption on a pressable cell. The card's
        /// numbers sit on <c>bg_shortcut</c>, which is light, so they are dark; the two
        /// pressable rows here sit on <c>button_action</c>, which is nearly black, and the
        /// card has no text of its own on that sprite to copy. Taking the number colour there
        /// puts dark on dark and the caption disappears — so those keep the face and lose the
        /// colour.
        /// </summary>
        internal static bool ApplyValueStyle(TMP_Text text, bool tint = true)
        {
            return ApplyTextStyle(text, ValueStyle, tint);
        }

        /// <summary>
        /// Everything that makes one text look like another except its size — the face
        /// included. The panel borrows the card's title face for anything it has not dressed,
        /// and the card sets its headers and its numbers in neither that face nor each other's.
        ///
        /// Alignment is deliberately not among them — see <see cref="CycleRow.RowAlignment"/>.
        /// </summary>
        private static bool ApplyTextStyle(TMP_Text text, TMP_Text template, bool tint = true)
        {
            if (text == null || template == null) return false;

            if (template.font != null) text.font = template.font;

            if (tint) text.color = template.color;
            text.fontStyle = template.fontStyle;
            text.characterSpacing = template.characterSpacing;
            return true;
        }

        private static Sprite Find(Image[] images, string name)
        {
            foreach (var image in images)
            {
                if (image != null && image.name == name) return image.sprite;
            }

            return null;
        }

        private static string Face(TMP_Text text)
        {
            return text != null && text.font != null ? text.font.name : "<null>";
        }

        private static string Tint(TMP_Text text)
        {
            return text != null ? text.color.ToString() : "<null>";
        }

        private static string Name(Sprite sprite)
        {
            return sprite != null ? sprite.name : "<null>";
        }

        /// <summary>
        /// Dresses a cell in one of the borrowed sprites, and reports whether it took.
        ///
        /// Sliced, because these sprites are tiny — <c>bg_shortcut</c> is 26x16 — and drawing
        /// one Simple across a 230-wide cell would smear its border through the middle. The
        /// multiplier stays at 1, which is what every Image on this screen uses, so a corner
        /// comes out the same size here as it does on the card.
        /// </summary>
        internal static bool Apply(Image image, Sprite sprite)
        {
            if (image == null || sprite == null) return false;

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = 1f;
            image.color = Color.white;
            return true;
        }
    }
}
