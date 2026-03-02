#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tiogiras.PVTM.Editor
{

/// <summary> Custom inspector used to draw <see cref="TextureProjection" /> </summary>
[CustomEditor(typeof(TextureProjection))]
public class TextureProjectorInspector : UnityEditor.Editor
{
    /// <summary> Simple data source to define the length of the displayed mip levels </summary>
    private readonly List<int> _levels = new();
    
    /// <summary> Stores the has value of the last update cycle </summary>
    private int _lastHash;
    
    /// <summary> Stores a reference to the list view displaying the mip levels </summary>
    private ListView _mipLevels;
    
    /// <summary> Stores a reference to the label displaying the full texture resolution </summary>
    private Label _requiredResolutionLabel;

    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();

        if (!Application.isPlaying)
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

        var customInfoPanel = new VisualElement
        {
            style =
            {
                paddingLeft = 10,
                paddingTop = 10
            }
        };

        root.Add(customInfoPanel);

        _requiredResolutionLabel = new Label { style = { color = Color.gray } };
        customInfoPanel.Add(_requiredResolutionLabel);

        _mipLevels = new ListView
        {
            virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
            makeItem = () =>
            {
                var row = new VisualElement
                {
                    style =
                    {
                        paddingTop = 2,
                        paddingBottom = 6
                    }
                };

                var title = new Label
                {
                    name = "title",
                    style =
                    {
                        unityFontStyleAndWeight = FontStyle.Bold,
                        color = Color.gray
                    }
                };
                row.Add(title);

                var panel = new VisualElement
                {
                    style = { paddingLeft = 10 },
                    name = "panel"
                };
                row.Add(panel);

                var pageCount = new Label
                {
                    name = "pageCount",
                    style = { color = Color.gray }
                };
                panel.Add(pageCount);

                var pageRes = new Label
                {
                    name = "pageRes",
                    style = { color = Color.gray }
                };
                panel.Add(pageRes);

                return row;
            },

            bindItem = (element, i) =>
            {
                var mp = (TextureProjection)target;

                var title = element.Q<Label>("title");
                var pageCount = element.Q<Label>("pageCount");
                var pageRes = element.Q<Label>("pageRes");

                title.text = $"Level {i}";
                pageCount.text = $"Page Count: {TextureProjection.PageCount(i)}";
                pageRes.text = $"Page Resolution: {mp.PageResolution()} x {mp.PageResolution()} px";
            },

            selectionType = SelectionType.None
        };

        customInfoPanel.Add(_mipLevels);

        ForceRefresh();

        root.schedule.Execute(() =>
        {
            if (serializedObject == null) return;

            serializedObject.Update();

            var hash = ComputeHash();
            if (hash == _lastHash)
                return;

            _lastHash = hash;
            RefreshGUI();
        }).Every(100);

        return root;
    }

    /// <summary>
    ///     Calculates a hash based on <see cref="TextureProjection._ppdTarget" />, <see cref="TextureProjection._roomSize" />
    ///     and <see cref="TextureProjection._mipCount" /> to check if any relevant values where changed since the last update
    /// </summary>
    private int ComputeHash()
    {
        var ppd = serializedObject.FindProperty("_ppdTarget")?.floatValue ?? 0f;
        var room = serializedObject.FindProperty("_size")?.floatValue ?? 0f;
        var mip = serializedObject.FindProperty("_mipCount")?.intValue ?? 0;

        unchecked
        {
            var h = 17;
            h = h * 31 + ppd.GetHashCode();
            h = h * 31 + room.GetHashCode();
            h = h * 31 + mip.GetHashCode();
            return h;
        }
    }

    /// <summary> Force a refresh of the inspector gui </summary>
    private void ForceRefresh()
    {
        serializedObject.Update();
        _lastHash = ComputeHash();
        RefreshGUI();
    }

    /// <summary> Refresh the inspector gui </summary>
    private void RefreshGUI()
    {
        var mp = (TextureProjection)target;
        var res = mp.CalculateRequiredResolution();
        _requiredResolutionLabel.text = $"Required Resolution: {res} x {res} px";

        _levels.Clear();

        for (var i = 0; i < mp.mipCount; i++)
            _levels.Add(i);

        _mipLevels.itemsSource = _levels;
        _mipLevels.RefreshItems();
    }
}
}
#endif