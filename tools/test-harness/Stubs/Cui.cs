// Mirror of Oxide.Game.Rust.Cui (Oxide.Rust). Property names/JSON keys follow the real library.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using UnityEngine.UI;
using HarnessRuntime;

namespace Oxide.Game.Rust.Cui
{
    public static class CuiHelper
    {
        public static string ToJson(List<CuiElement> elements, bool format = false)
            => JsonConvert.SerializeObject(elements, format ? Formatting.Indented : Formatting.None,
                new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore }).Replace("\\n", "\n");

        public static List<CuiElement> FromJson(string json) => JsonConvert.DeserializeObject<List<CuiElement>>(json);
        public static string GetGuid() => Guid.NewGuid().ToString().Replace("-", string.Empty);

        public static bool AddUi(BasePlayer player, List<CuiElement> elements)
            => AddUi(player, JsonConvert.SerializeObject(elements, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore }));

        public static bool AddUi(BasePlayer player, string json)
        {
            if (player?.net == null) return false;
            Sim.ClientAddUi(player, json);
            return true;
        }

        public static bool DestroyUi(BasePlayer player, string elem)
        {
            if (player?.net == null) return false;
            Sim.ClientDestroyUi(player, elem);
            return true;
        }
    }

    public class CuiElementContainer : List<CuiElement>
    {
        public string Add(CuiButton button, string parent = "Hud", string name = null, string destroyUi = null)
        {
            if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();
            Add(new CuiElement { Name = name, Parent = parent, FadeOut = button.FadeOut, DestroyUi = destroyUi, Components = { button.Button, button.RectTransform } });
            if (!string.IsNullOrEmpty(button.Text.Text))
                Add(new CuiElement { Parent = name, FadeOut = button.FadeOut, Components = { button.Text, new CuiRectTransformComponent() } });
            return name;
        }

        public string Add(CuiLabel label, string parent = "Hud", string name = null, string destroyUi = null)
        {
            if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();
            Add(new CuiElement { Name = name, Parent = parent, FadeOut = label.FadeOut, DestroyUi = destroyUi, Components = { label.Text, label.RectTransform } });
            return name;
        }

        public string Add(CuiPanel panel, string parent = "Hud", string name = null, string destroyUi = null)
        {
            if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();
            var element = new CuiElement { Name = name, Parent = parent, FadeOut = panel.FadeOut, DestroyUi = destroyUi };
            if (panel.Image != null) element.Components.Add(panel.Image);
            if (panel.RawImage != null) element.Components.Add(panel.RawImage);
            element.Components.Add(panel.RectTransform);
            if (panel.CursorEnabled) element.Components.Add(new CuiNeedsCursorComponent());
            if (panel.KeyboardEnabled) element.Components.Add(new CuiNeedsKeyboardComponent());
            Add(element);
            return name;
        }

        public string ToJson() => ToString();
        public override string ToString() => CuiHelper.ToJson(this);
    }

    public class CuiButton
    {
        public CuiButtonComponent Button { get; } = new CuiButtonComponent();
        public CuiRectTransformComponent RectTransform { get; } = new CuiRectTransformComponent();
        public CuiTextComponent Text { get; } = new CuiTextComponent();
        public float FadeOut { get; set; }
    }

    public class CuiPanel
    {
        public CuiImageComponent Image { get; set; } = new CuiImageComponent();
        public CuiRawImageComponent RawImage { get; set; }
        public CuiRectTransformComponent RectTransform { get; } = new CuiRectTransformComponent();
        public bool CursorEnabled { get; set; }
        public bool KeyboardEnabled { get; set; }
        public float FadeOut { get; set; }
    }

    public class CuiLabel
    {
        public CuiTextComponent Text { get; } = new CuiTextComponent();
        public CuiRectTransformComponent RectTransform { get; } = new CuiRectTransformComponent();
        public float FadeOut { get; set; }
    }

    public class CuiElement
    {
        [DefaultValue("AddUI CreatedPanel")]
        [JsonProperty("name")]
        public string Name { get; set; } = "AddUI CreatedPanel";

        [JsonProperty("parent")]
        public string Parent { get; set; } = "Hud";

        [JsonProperty("destroyUi", NullValueHandling = NullValueHandling.Ignore)]
        public string DestroyUi { get; set; }

        [JsonProperty("components")]
        public List<ICuiComponent> Components { get; } = new List<ICuiComponent>();

        [JsonProperty("fadeOut")]
        public float FadeOut { get; set; }

        [JsonProperty("update", NullValueHandling = NullValueHandling.Ignore)]
        public bool Update { get; set; }
    }

    public interface ICuiComponent
    {
        [JsonProperty("type")]
        string Type { get; }
    }

    public interface ICuiColor
    {
        [DefaultValue("1.0 1.0 1.0 1.0")]
        [JsonProperty("color")]
        string Color { get; set; }
    }

    public class CuiTextComponent : ICuiComponent, ICuiColor
    {
        public string Type => "UnityEngine.UI.Text";
        [JsonProperty("text")] public string Text { get; set; }
        [DefaultValue(14)] [JsonProperty("fontSize")] public int FontSize { get; set; }
        [DefaultValue("RobotoCondensed-Bold.ttf")] [JsonProperty("font")] public string Font { get; set; }
        [DefaultValue(TextAnchor.UpperLeft)] [JsonConverter(typeof(StringEnumConverter))] [JsonProperty("align")] public TextAnchor Align { get; set; }
        public string Color { get; set; }
        [DefaultValue(VerticalWrapMode.Truncate)] [JsonConverter(typeof(StringEnumConverter))] [JsonProperty("verticalOverflow")] public VerticalWrapMode VerticalOverflow { get; set; }
        [JsonProperty("fadeIn")] public float FadeIn { get; set; }
    }

    public class CuiImageComponent : ICuiComponent, ICuiColor
    {
        public string Type => "UnityEngine.UI.Image";
        [DefaultValue("Assets/Content/UI/UI.Background.Tile.psd")] [JsonProperty("sprite")] public string Sprite { get; set; }
        [DefaultValue("Assets/Icons/IconMaterial.mat")] [JsonProperty("material")] public string Material { get; set; }
        public string Color { get; set; }
        [DefaultValue(Image.Type.Simple)] [JsonConverter(typeof(StringEnumConverter))] [JsonProperty("imagetype")] public Image.Type ImageType { get; set; }
        [JsonProperty("png")] public string Png { get; set; }
        [JsonProperty("fadeIn")] public float FadeIn { get; set; }
        [JsonProperty("itemid")] public int ItemId { get; set; }
        [JsonProperty("skinid")] public ulong SkinId { get; set; }
    }

    public class CuiRawImageComponent : ICuiComponent, ICuiColor
    {
        public string Type => "UnityEngine.UI.RawImage";
        [DefaultValue("Assets/Icons/rust.png")] [JsonProperty("sprite")] public string Sprite { get; set; }
        public string Color { get; set; }
        [JsonProperty("material")] public string Material { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
        [JsonProperty("png")] public string Png { get; set; }
        [JsonProperty("steamid")] public string SteamId { get; set; }
        [JsonProperty("fadeIn")] public float FadeIn { get; set; }
    }

    public class CuiButtonComponent : ICuiComponent, ICuiColor
    {
        public string Type => "UnityEngine.UI.Button";
        [JsonProperty("command")] public string Command { get; set; }
        [JsonProperty("close")] public string Close { get; set; }
        [DefaultValue("Assets/Content/UI/UI.Background.Tile.psd")] [JsonProperty("sprite")] public string Sprite { get; set; }
        [DefaultValue("Assets/Icons/IconMaterial.mat")] [JsonProperty("material")] public string Material { get; set; }
        public string Color { get; set; }
        [DefaultValue(Image.Type.Simple)] [JsonConverter(typeof(StringEnumConverter))] [JsonProperty("imagetype")] public Image.Type ImageType { get; set; }
        [JsonProperty("fadeIn")] public float FadeIn { get; set; }
    }

    public class CuiOutlineComponent : ICuiComponent, ICuiColor
    {
        public string Type => "UnityEngine.UI.Outline";
        public string Color { get; set; }
        [DefaultValue("1.0 -1.0")] [JsonProperty("distance")] public string Distance { get; set; }
        [JsonProperty("useGraphicAlpha")] public bool UseGraphicAlpha { get; set; }
    }

    public class CuiInputFieldComponent : ICuiComponent, ICuiColor
    {
        public string Type => "UnityEngine.UI.InputField";
        [DefaultValue("")] [JsonProperty("text")] public string Text { get; set; } = string.Empty;
        [DefaultValue(14)] [JsonProperty("fontSize")] public int FontSize { get; set; }
        [DefaultValue("RobotoCondensed-Bold.ttf")] [JsonProperty("font")] public string Font { get; set; }
        [DefaultValue(TextAnchor.UpperLeft)] [JsonConverter(typeof(StringEnumConverter))] [JsonProperty("align")] public TextAnchor Align { get; set; }
        public string Color { get; set; }
        [DefaultValue(100)] [JsonProperty("characterLimit")] public int CharsLimit { get; set; }
        [JsonProperty("command")] public string Command { get; set; }
        [DefaultValue(false)] [JsonProperty("password")] public bool IsPassword { get; set; }
        [JsonProperty("readOnly")] public bool ReadOnly { get; set; }
        [JsonProperty("needsKeyboard")] public bool NeedsKeyboard { get; set; }
        [DefaultValue(InputField.LineType.SingleLine)] [JsonConverter(typeof(StringEnumConverter))] [JsonProperty("lineType")] public InputField.LineType LineType { get; set; }
        [JsonProperty("autofocus")] public bool Autofocus { get; set; }
        [JsonProperty("hudMenuInput")] public bool HudMenuInput { get; set; }
    }

    // Older Oxide builds use int for these, newer ones float; int here so only values valid in both compile.
    public class CuiCountdownComponent : ICuiComponent
    {
        public string Type => "Countdown";
        [JsonProperty("endTime")] public int EndTime { get; set; }
        [JsonProperty("startTime")] public int StartTime { get; set; }
        [JsonProperty("step")] public int Step { get; set; }
        [JsonProperty("command")] public string Command { get; set; }
        [JsonProperty("fadeIn")] public float FadeIn { get; set; }
    }

    public class CuiNeedsCursorComponent : ICuiComponent { public string Type => "NeedsCursor"; }
    public class CuiNeedsKeyboardComponent : ICuiComponent { public string Type => "NeedsKeyboard"; }

    public class CuiRectTransform
    {
        [DefaultValue("0.0 0.0")] [JsonProperty("anchormin")] public string AnchorMin { get; set; }
        [DefaultValue("1.0 1.0")] [JsonProperty("anchormax")] public string AnchorMax { get; set; }
        [DefaultValue("0.0 0.0")] [JsonProperty("offsetmin")] public string OffsetMin { get; set; }
        [DefaultValue("0.0 0.0")] [JsonProperty("offsetmax")] public string OffsetMax { get; set; }
    }

    public class CuiRectTransformComponent : CuiRectTransform, ICuiComponent
    {
        public string Type => "RectTransform";
    }

    public class CuiScrollbar
    {
        [JsonProperty("invert")] public bool Invert { get; set; }
        [JsonProperty("autoHide")] public bool AutoHide { get; set; }
        [JsonProperty("handleSprite")] public string HandleSprite { get; set; }
        [JsonProperty("size")] public float Size { get; set; }
        [JsonProperty("handleColor")] public string HandleColor { get; set; }
        [JsonProperty("highlightColor")] public string HighlightColor { get; set; }
        [JsonProperty("pressedColor")] public string PressedColor { get; set; }
        [JsonProperty("trackSprite")] public string TrackSprite { get; set; }
        [JsonProperty("trackColor")] public string TrackColor { get; set; }
    }

    public class CuiScrollViewComponent : ICuiComponent
    {
        public string Type => "UnityEngine.UI.ScrollView";
        [JsonProperty("contentTransform")] public CuiRectTransform ContentTransform { get; set; }
        [JsonProperty("horizontal")] public bool Horizontal { get; set; }
        [JsonProperty("vertical")] public bool Vertical { get; set; }
        [JsonConverter(typeof(StringEnumConverter))] [JsonProperty("movementType")] public ScrollRect.MovementType MovementType { get; set; }
        [JsonProperty("elasticity")] public float Elasticity { get; set; }
        [JsonProperty("inertia")] public bool Inertia { get; set; }
        [JsonProperty("decelerationRate")] public float DecelerationRate { get; set; }
        [JsonProperty("scrollSensitivity")] public float ScrollSensitivity { get; set; }
        [JsonProperty("horizontalScrollbar")] public CuiScrollbar HorizontalScrollbar { get; set; }
        [JsonProperty("verticalScrollbar")] public CuiScrollbar VerticalScrollbar { get; set; }
    }
}
