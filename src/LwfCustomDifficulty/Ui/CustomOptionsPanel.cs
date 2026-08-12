using LwfCustomDifficulty.Patches;
using Scene.TitleScene;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LwfCustomDifficulty.Ui
{
    /// <summary>
    /// The eight Custom rules, beside the difficulty card. Every edit goes through
    /// <see cref="PluginConfig.Set"/>, which normalises and saves, and every row reads the
    /// stored value back afterwards, so the panel holds no state of its own.
    ///
    /// Text is formatted and parsed in <see cref="System.Globalization.CultureInfo.CurrentCulture"/>
    /// throughout, by way of <see cref="NumericText"/>: that is the culture the input field
    /// validates typed characters against, and the three have to agree.
    /// </summary>
    internal static class CustomOptionsPanel
    {
        /// <summary>The card is 630x630 and already full, so the panel sits beside it
        /// rather than inside it. 630 across clears the card's arrow buttons, which
        /// overhang its edges by 20. Negative puts it on the left: the panel then spans
        /// -910..-350, clearing the 1920 canvas edge by 50.</summary>
        private const float PanelWidth = 560f;
        private const float PanelHeight = 630f;
        private const float PanelOffsetX = -630f;

        private static GameObject _root;
        private static DifficultySetter _owner;
        private static TMP_FontAsset _font;

        internal static void Attach(DifficultySetter setter, TMP_FontAsset font)
        {
            // Before Build, which dresses each cell as it creates it. Resolving off the same
            // setter the font comes from leaves the whole panel depending on one live object
            // and on no asset path at all.
            GameSkin.Resolve(setter);

            if (_root == null)
            {
                Build(setter, font);
            }
            else if (_owner != setter)
            {
                // One panel, and a setter is destroyed with its scene. Following the setter
                // being set keeps the panel inside the card that is actually on screen.
                Reparent(setter);
            }

            // Applied on every call, not only the first: the rows outlive any one Attach and
            // the font asset arrives from the card's own title text, which the scene owns.
            ApplyFont(font);
        }

        internal static void SetVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }

        private static void Build(DifficultySetter setter, TMP_FontAsset font)
        {
            _root = new GameObject("CustomOptions", typeof(RectTransform), typeof(Image),
                                   typeof(VerticalLayoutGroup));

            // The card's own body sprite, so the panel reads as a second card rather than as
            // a dark slab laid over the scene. Falls back to the old wash only if the card's
            // background could not be found.
            var background = _root.GetComponent<Image>();
            if (!GameSkin.Apply(background, GameSkin.Panel))
            {
                background.color = new Color(0f, 0f, 0f, 0.55f);
            }

            var layout = _root.GetComponent<VerticalLayoutGroup>();

            // 29 a side is what is left of 560 once the rows have their 502, and it is close
            // enough to the 37 the frame's corner occupies that the content sits inside the
            // border rather than under it. Taking the exact remainder rather than a round 30
            // keeps the rows at their stated widths: a horizontal layout short of its
            // children's preferred width shrinks them all in proportion, and the value column
            // is sized to the text it has to hold.
            //
            // Vertically the eight rows need 8x60 plus seven 10s of spacing, so 28 top and
            // bottom fits 606 into 630.
            layout.padding = new RectOffset(29, 29, 28, 28);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            Reparent(setter);

            InputRow.Create(_root.transform, "Time Limit", font,
                () => NumericText.Format(PluginConfig.TimeLimitMinutes),
                v => Commit(timeLimit: NumericText.ParseInt(v, PluginConfig.TimeLimitMinutes)));
            InputRow.Create(_root.transform, "Repayments", font,
                () => NumericText.Format(PluginConfig.Repayments),
                v => Commit(repayments: NumericText.ParseInt(v, PluginConfig.Repayments)));
            InputRow.Create(_root.transform, "First Repayment", font,
                () => NumericText.Format(PluginConfig.Rules.FirstRepayment),
                v => Commit(firstRepayment: NumericText.ParseInt(v, PluginConfig.Rules.FirstRepayment)));
            CycleRow.Create(_root.transform, "Growth", font,
                () => PluginConfig.Rules.Mode.ToString(),
                () => Commit(mode: Next(PluginConfig.Rules.Mode)));
            InputRow.Create(_root.transform, "Growth Amount", font,
                () => NumericText.Format(PluginConfig.Rules.GrowthAmount),
                v => Commit(growthAmount: NumericText.ParseDouble(v, PluginConfig.Rules.GrowthAmount)),
                decimals: true);
            InputRow.Create(_root.transform, "Surcharge", font,
                () => NumericText.Format(PluginConfig.Rules.Surcharge),
                v => Commit(surcharge: NumericText.ParseInt(v, PluginConfig.Rules.Surcharge)));
            InputRow.Create(_root.transform, "Surcharge Every", font,
                () => NumericText.Format(PluginConfig.Rules.SurchargeEvery),
                v => Commit(surchargeEvery: NumericText.ParseInt(v, PluginConfig.Rules.SurchargeEvery)));
            ToggleRow.Create(_root.transform, "Taxes", font,
                () => PluginConfig.TaxesEnabled,
                on => Commit(taxes: on));

            SetVisible(CustomDifficulty.IsCustom(setter.ToSetDifficulty));
        }

        private static void ApplyFont(TMP_FontAsset font)
        {
            if (font == null || font == _font) return;
            _font = font;

            foreach (var text in _root.GetComponentsInChildren<TMP_Text>(includeInactive: true))
            {
                // Row labels follow the card's header face, which is a different asset from
                // the title face this method carries; GameSkin has already set it on them.
                if (text.name == CycleRow.HeaderTextName && GameSkin.LabelStyle != null) continue;

                text.font = font;
            }
        }

        private static void Reparent(DifficultySetter setter)
        {
            _owner = setter;

            // The setter's parent, not the setter: it is the container the selection
            // screen shows and hides, so the panel travels with the card without landing
            // inside the card's own full 630x630 body.
            var host = setter.transform.parent != null ? setter.transform.parent : setter.transform;
            _root.transform.SetParent(host, worldPositionStays: false);

            var rect = _root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            rect.anchoredPosition = new Vector2(PanelOffsetX, 0f);
            rect.localScale = Vector3.one;
        }

        private static GrowthMode Next(GrowthMode mode)
        {
            return mode == GrowthMode.Exponential ? GrowthMode.Linear : mode + 1;
        }

        private static void Commit(int? timeLimit = null, int? repayments = null, int? firstRepayment = null,
                                   GrowthMode? mode = null, double? growthAmount = null,
                                   int? surcharge = null, int? surchargeEvery = null, bool? taxes = null)
        {
            var r = PluginConfig.Rules;
            PluginConfig.Set(
                timeLimit      ?? PluginConfig.TimeLimitMinutes,
                repayments     ?? PluginConfig.Repayments,
                firstRepayment ?? r.FirstRepayment,
                mode           ?? r.Mode,
                growthAmount   ?? r.GrowthAmount,
                surcharge      ?? r.Surcharge,
                surchargeEvery ?? r.SurchargeEvery,
                taxes          ?? PluginConfig.TaxesEnabled);

            // The card's numbers are written only when the selection changes, and an edit
            // here changes the numbers without changing the selection. Without this the card
            // would keep the values it was last built with until the player cycled the
            // carousel away and back.
            UpdateDifficultyTextPatch.Apply(_owner);
        }
    }
}
