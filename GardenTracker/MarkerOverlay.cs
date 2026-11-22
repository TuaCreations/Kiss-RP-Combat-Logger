using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using GardenTracker.Windows;

namespace GardenTracker;

/// <summary>
/// Draws session markers in the 3D world: a ground ring at the marker's radius plus a floating
/// name and HP bar. Purely local drawing; nothing is sent to the game server.
/// </summary>
public sealed class MarkerOverlay
{
    private readonly Plugin p;
    private const int Segments = 40;
    private const float LabelHeight = 2.6f; // yalms above the ring centre

    public MarkerOverlay(Plugin plugin) => p = plugin;

    public void Draw()
    {
        if (!p.Configuration.ShowOverlay) return;
        var s = p.Sync.Current;
        if (s == null || s.Markers.Count == 0) return;
        if (Plugin.ObjectTable.LocalPlayer == null) return;

        var territory = (int)Plugin.ClientState.TerritoryType;
        var draw = ImGui.GetBackgroundDrawList();

        foreach (var m in s.Markers)
        {
            if (m.Territory != territory) continue;
            var enemy = m.EnemyId.HasValue ? s.Enemies.FirstOrDefault(e => e.Id == m.EnemyId.Value) : null;
            if (enemy != null && !enemy.Visible) continue; // hidden enemies keep their marker secret

            var defeated = enemy != null && enemy.HpMax > 0 && enemy.Hp <= 0;
            var col = ColorOf(defeated ? "grey" : m.Color);
            var center = new Vector3(m.X, m.Y, m.Z);

            DrawRing(draw, center, m.Radius, ImGui.GetColorU32(col with { W = 0.9f }), 2.5f);
            DrawRing(draw, center, m.Radius * 0.96f, ImGui.GetColorU32(col with { W = 0.25f }), 6f);

            if (!Plugin.GameGui.WorldToScreen(center + new Vector3(0, LabelHeight, 0), out var top)) continue;

            var label = enemy?.Name ?? m.Label;
            if (defeated) label += " (defeated)";
            var size = ImGui.CalcTextSize(label);
            var textPos = new Vector2(top.X - size.X / 2, top.Y - size.Y - 6);
            draw.AddRectFilled(textPos - new Vector2(4, 2), textPos + size + new Vector2(4, 2), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)), 4f);
            draw.AddText(textPos, ImGui.GetColorU32(Ui.Gold), label);

            var hpVisible = s.Session.ShowEnemyHp || p.IsGm;
            if (enemy != null && enemy.HpMax > 0 && p.Configuration.ShowOverlayHp && !hpVisible)
            {
                var word = Ui.HpWord(enemy.Hp, enemy.HpMax);
                var ws = ImGui.CalcTextSize(word);
                draw.AddText(new Vector2(top.X - ws.X / 2, top.Y), ImGui.GetColorU32(Ui.HpColor(enemy.Hp, enemy.HpMax)), word);
            }
            else if (enemy != null && enemy.HpMax > 0 && p.Configuration.ShowOverlayHp)
            {
                const float w = 110f, h = 9f;
                var barMin = new Vector2(top.X - w / 2, top.Y);
                var frac = Math.Clamp((float)enemy.Hp / enemy.HpMax, 0f, 1f);
                draw.AddRectFilled(barMin, barMin + new Vector2(w, h), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.7f)), 3f);
                draw.AddRectFilled(barMin, barMin + new Vector2(w * frac, h), ImGui.GetColorU32(Ui.HpColor(enemy.Hp, enemy.HpMax)), 3f);
                var hpText = $"{enemy.Hp}/{enemy.HpMax}";
                var hs = ImGui.CalcTextSize(hpText);
                draw.AddText(new Vector2(top.X - hs.X / 2, top.Y + h + 1), ImGui.GetColorU32(new Vector4(1, 1, 1, 0.9f)), hpText);
            }
        }
    }

    private static void DrawRing(ImDrawListPtr draw, Vector3 center, float radius, uint color, float thickness)
    {
        Vector2? prev = null;
        for (var i = 0; i <= Segments; i++)
        {
            var a = i * MathF.Tau / Segments;
            var world = center + new Vector3(MathF.Cos(a) * radius, 0, MathF.Sin(a) * radius);
            if (Plugin.GameGui.WorldToScreen(world, out var screen))
            {
                if (prev.HasValue) draw.AddLine(prev.Value, screen, color, thickness);
                prev = screen;
            }
            else
            {
                prev = null; // point is behind the camera; break the ring here
            }
        }
    }

    private static Vector4 ColorOf(string name) => name switch
    {
        "violet" => Ui.Violet,
        "gold" => Ui.Gold,
        "green" => Ui.Green,
        "blue" => new Vector4(0.35f, 0.6f, 1f, 1f),
        "white" => new Vector4(1f, 1f, 1f, 1f),
        "grey" => Ui.Grey,
        _ => Ui.Red,
    };
}
