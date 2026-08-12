using System.Reflection;
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

            // A serialized field rather than a child name: it is the card's own reference to
            // the button, and it survives the GameObject being renamed.
            var left = LeftButtonField?.GetValue(setter) as Button;
            Button = left != null && left.image != null ? left.image.sprite : null;
            if (left != null) Colors = left.colors;

            Resolved = Panel != null && ValueCell != null && Button != null;

            // The resolved values, not the intent: a name that stopped matching shows up here
            // as a null instead of quietly reverting the panel to grey.
            Plugin.Log.LogInfo($"Skin: panel={Name(Panel)} value={Name(ValueCell)} "
                               + $"button={Name(Button)} normal={Colors.normalColor} "
                               + $"highlighted={Colors.highlightedColor} pressed={Colors.pressedColor} "
                               + $"resolved={Resolved}");

            if (!Resolved)
            {
                Plugin.Log.LogWarning("Skin: incomplete; unresolved cells keep their untextured fill.");
            }
        }

        private static Sprite Find(Image[] images, string name)
        {
            foreach (var image in images)
            {
                if (image != null && image.name == name) return image.sprite;
            }

            return null;
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
