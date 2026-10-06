using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CabinetLooter
{
    /// <summary>
    /// Click handling for a heading, deliberately not a <see cref="Button"/>.
    ///
    /// A Button is a Selectable: clicking it makes it the EventSystem's selected object, and the
    /// input module then sends it navigation (Move) events from the Horizontal/Vertical axes, which
    /// are WASD. With Inventory Walker the player walks with WASD while the panel is open, so
    /// every step navigated the selection on to the panel's scrollbar and moved it, fighting the
    /// mouse wheel. A plain pointer-click handler is never selected and never navigated, and it
    /// does not handle scroll, so the wheel still bubbles up to the scroll area.
    /// </summary>
    internal sealed class HeadingClick : MonoBehaviour, IPointerClickHandler
    {
        public Action Clicked;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                Clicked?.Invoke();
            }
        }
    }

    /// <summary>
    /// A drawer's heading: a small two-line bar above the drawer's grid, its name on top and its
    /// search state underneath. Two lines because drawers stand side by side, a cabinet to a row,
    /// so each heading is only a column wide.
    /// </summary>
    internal sealed class DrawerHeading
    {
        public static readonly Color LabelColor = new Color(0.77f, 0.76f, 0.70f);
        public static readonly Color SearchingColor = new Color(0.90f, 0.78f, 0.35f);
        public static readonly Color NotSearchedColor = new Color(0.60f, 0.60f, 0.58f);
        public static readonly Color PartlyColor = new Color(0.90f, 0.55f, 0.25f);
        public static readonly Color EmptyColor = new Color(0.45f, 0.45f, 0.44f);
        public static readonly Color FoundColor = new Color(0.62f, 0.62f, 0.58f);

        public const float Height = 32f;

        public GameObject Root;
        private RectTransform _bar;
        private TextMeshProUGUI _label;
        private TextMeshProUGUI _state;
        private string _labelText;
        private string _stateText;

        /// <param name="highlighted">The drawer the player opened: a brighter bar.</param>
        public static DrawerHeading Create(Transform parent, TextMeshProUGUI fontSource, float width, bool highlighted, Action clicked)
        {
            GameObject root = NewRect("CabinetLooter Heading", parent, width, Height);
            root.AddComponent<HeadingClick>().Clicked = clicked;

            // What is drawn sits on a child filling the root, so it can be nudged sideways to line
            // up with the grid below (see AlignTo) while the layout group keeps placing the root.
            var bar = new GameObject("Bar", typeof(RectTransform));
            bar.transform.SetParent(root.transform, false);
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = Vector2.zero;
            barRect.anchorMax = Vector2.one;
            barRect.offsetMin = Vector2.zero;
            barRect.offsetMax = Vector2.zero;

            Image background = bar.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, highlighted ? 0.13f : 0.06f);
            background.raycastTarget = true;

            var heading = new DrawerHeading
            {
                Root = root,
                _bar = barRect,
                _label = NewText("Label", bar.transform, fontSource, 12f, TextAlignmentOptions.BottomLeft, 0.5f, 1f),
                _state = NewText("State", bar.transform, fontSource, 10f, TextAlignmentOptions.TopLeft, 0f, 0.5f)
            };
            heading._label.color = LabelColor;
            return heading;
        }

        /// <summary>One cabinet's drawers, side by side, top-left aligned.</summary>
        public static GameObject CreateRow(Transform parent, float spacing)
        {
            var go = new GameObject("CabinetLooter Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            HorizontalLayoutGroup row = go.AddComponent<HorizontalLayoutGroup>();
            row.spacing = spacing;
            row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            return go;
        }

        /// <summary>One drawer: its heading above its grid, a fixed column wide.</summary>
        public static GameObject CreateColumn(Transform parent, float width)
        {
            var go = new GameObject("CabinetLooter Drawer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            VerticalLayoutGroup column = go.AddComponent<VerticalLayoutGroup>();
            column.spacing = 2f;
            column.childAlignment = TextAnchor.UpperLeft;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;

            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.minWidth = width;
            layout.preferredWidth = width;
            return go;
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>
        /// How far, in the heading's own units, the left edge of <paramref name="target"/> sits to
        /// the right of the heading's (unshifted) left edge.
        /// </summary>
        public float LeftEdgeGap(RectTransform target)
        {
            var root = (RectTransform)Root.transform;
            float scale = root.lossyScale.x;
            if (target == null || scale <= 0f)
            {
                return 0f;
            }
            target.GetWorldCorners(Corners);
            float targetLeft = Corners[0].x;
            root.GetWorldCorners(Corners);
            return (targetLeft - Corners[0].x) / scale;
        }

        /// <summary>Slides the bar sideways by <paramref name="shift"/>, keeping its width.</summary>
        public void Shift(float shift)
        {
            if (Mathf.Abs(_bar.offsetMin.x - shift) < 0.01f)
            {
                return;
            }
            _bar.offsetMin = new Vector2(shift, 0f);
            _bar.offsetMax = new Vector2(shift, 0f);
        }

        public void Set(string label, string state, Color stateColor)
        {
            if (label != _labelText)
            {
                _labelText = label;
                _label.text = label;
            }
            if (state != _stateText)
            {
                _stateText = state;
                _state.text = state ?? string.Empty;
            }
            _state.color = stateColor;
        }

        /// <summary>A cabinet's title in cluster mode: text only, no bar, a little larger.</summary>
        public static GameObject CreateTitle(Transform parent, TextMeshProUGUI fontSource, float width, string text)
        {
            GameObject root = NewRect("CabinetLooter Title", parent, width, 22f);
            TextMeshProUGUI title = NewText("Text", root.transform, fontSource, 13f, TextAlignmentOptions.BottomLeft);
            title.color = LabelColor;
            title.fontStyle = FontStyles.Bold;
            title.text = text;
            return root;
        }

        /// <summary>
        /// An invisible raycast target filling its parent and ignored by layout, for the scroll
        /// wheel to land on over empty space.
        /// </summary>
        public static GameObject CreateScrollCatcher(Transform parent)
        {
            var go = new GameObject("CabinetLooter ScrollCatcher", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            go.AddComponent<LayoutElement>().ignoreLayout = true;

            Image image = go.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
            return go;
        }

        public static GameObject CreateSpacer(Transform parent, float height)
        {
            return NewRect("CabinetLooter Spacer", parent, 1f, height);
        }

        private static GameObject NewRect(string name, Transform parent, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(width, height);

            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.preferredWidth = width;
            return go;
        }

        /// <param name="bandBottom">Bottom of the text's band, as a fraction of the parent's height.</param>
        /// <param name="bandTop">Top of the text's band, as a fraction of the parent's height.</param>
        private static TextMeshProUGUI NewText(string name, Transform parent, TextMeshProUGUI fontSource, float size, TextAlignmentOptions alignment,
            float bandBottom = 0f, float bandTop = 1f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, bandBottom);
            rect.anchorMax = new Vector2(1f, bandTop);
            rect.offsetMin = new Vector2(6f, 0f);
            rect.offsetMax = new Vector2(-6f, 0f);

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            if (fontSource != null)
            {
                text.font = fontSource.font;
                text.fontSharedMaterial = fontSource.fontSharedMaterial;
            }
            text.fontSize = size;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }
    }
}
