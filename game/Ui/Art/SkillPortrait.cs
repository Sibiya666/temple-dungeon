using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Skill artwork covers its frame. Captions and interaction belong to the parent.</summary>
public static class SkillPortrait
{
    public static TextureRect Create(Texture2D? texture = null)
    {
        var portrait = new TextureRect
        {
            Name = "SkillIcon",
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
            ClipContents = true,
        };
        portrait.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        // Leave only the one-pixel frame visible; no button content margins or icon size cap.
        (portrait.OffsetLeft, portrait.OffsetTop, portrait.OffsetRight, portrait.OffsetBottom) = (1, 1, -1, -1);
        return portrait;
    }
}
