using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace GardenTracker.Windows;

/// <summary>Small ImGui helpers shared by the windows.</summary>
public static class Ui
{
    public static readonly Vector4 Green = new(0.42f, 0.66f, 0.31f, 1f);   // Garden green
    public static readonly Vector4 Gold = new(1f, 0.85f, 0.40f, 1f);      // Garden gold
    public static readonly Vector4 Red = new(0.85f, 0.30f, 0.30f, 1f);
    public static readonly Vector4 Violet = new(0.62f, 0.45f, 0.95f, 1f); // void / Momentum
    public static readonly Vector4 Grey = new(0.65f, 0.65f, 0.65f, 1f);

    // Edit buffers keep a field stable while the user is typing, even though the snapshot refreshes every second.
    private static readonly Dictionary<string, string> TextBuffers = new();
    private static readonly Dictionary<string, int> IntBuffers = new();

    /// <summary>Text field bound to server data. Returns true once, when the user finishes an edit.</summary>
    public static bool EditText(string id, string current, out string value, float width = 160f, int maxLength = 256)
    {
        if (!TextBuffers.TryGetValue(id, out var buf)) buf = current;
        ImGui.SetNextItemWidth(width);
        ImGui.InputText($"##{id}", ref buf, maxLength);
        var commit = ImGui.IsItemDeactivatedAfterEdit();
        if (ImGui.IsItemActive()) TextBuffers[id] = buf; else TextBuffers.Remove(id);
        value = buf;
        return commit;
    }

    public static bool EditMultiline(string id, string current, out string value, Vector2 size, int maxLength = 2000)
    {
        if (!TextBuffers.TryGetValue(id, out var buf)) buf = current;
        ImGui.InputTextMultiline($"##{id}", ref buf, maxLength, size);
        var commit = ImGui.IsItemDeactivatedAfterEdit();
        if (ImGui.IsItemActive()) TextBuffers[id] = buf; else TextBuffers.Remove(id);
        value = buf;
        return commit;
    }

    /// <summary>Number field bound to server data. Returns true when the user finishes an edit.</summary>
    public static bool EditInt(string id, int current, out int value, float width = 60f)
    {
        if (!IntBuffers.TryGetValue(id, out var buf)) buf = current;
        ImGui.SetNextItemWidth(width);
        ImGui.InputInt($"##{id}", ref buf, 0, 0);
        var commit = ImGui.IsItemDeactivatedAfterEdit();
        if (ImGui.IsItemActive()) IntBuffers[id] = buf; else IntBuffers.Remove(id);
        value = buf;
        return commit;
    }

    public static void Bar(float fraction, string overlay, Vector4 color, float width = -1f, float height = 18f)
    {
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar(Math.Clamp(fraction, 0f, 1f), new Vector2(width, height), overlay);
        ImGui.PopStyleColor();
    }

    public static Vector4 HpColor(int hp, int max)
    {
        if (max <= 0) return Grey;
        var f = (float)hp / max;
        return f > 0.5f ? Green : f > 0.25f ? Gold : Red;
    }

    public static void Header(string text)
    {
        ImGui.Spacing();
        ImGui.TextColored(Gold, text);
        ImGui.Separator();
    }

    public static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(text);
    }
}
