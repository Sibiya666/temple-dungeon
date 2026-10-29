using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// Base of the debug panels that draw from a <see cref="SandboxSession"/>. Call <see cref="InitSession"/> before the panel
/// enters the tree; override <see cref="DrawContent"/> to fill <see cref="Content"/> (cleared before every draw).
/// Redraws are always deferred (<see cref="RequestRedraw"/>): a control's handler runs a session command, the session
/// raises Changed synchronously, and freeing that control (or an OptionButton's popup) while its signal is still firing
/// breaks Godot. Several requests in one frame give one redraw. Changed is subscribed in _EnterTree and dropped in
/// _ExitTree.
/// </summary>
public partial class SessionPanel : VBoxContainer
{
    private bool _redrawPending;
    private bool _subscribed;

    protected SandboxSession Session { get; private set; } = null!;

    /// <summary>The cleared-and-redrawn part of the panel (inside a scroll container), or null for a panel built once.</summary>
    protected VBoxContainer? Content { get; private set; }

    /// <summary>True while a deferred redraw is queued.</summary>
    public bool RedrawPending => _redrawPending;

    /// <summary>Sets the session; <paramref name="withContent"/> adds the scrolled <see cref="Content"/> box.</summary>
    protected void InitSession(SandboxSession session, bool withContent = true)
    {
        Session = session;
        if (!withContent) return;
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);
        Content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(Content);
    }

    public override void _EnterTree()
    {
        if (Session == null || _subscribed) return;
        Session.Changed += OnSessionChanged;
        _subscribed = true;
        RequestRedraw();
    }

    public override void _ExitTree()
    {
        if (!_subscribed) return;
        Session.Changed -= OnSessionChanged;
        _subscribed = false;
    }

    /// <summary>Called synchronously on every session change; by default queues a redraw.</summary>
    protected virtual void OnSessionChanged() => RequestRedraw();

    /// <summary>Queues one redraw at the end of the frame (no-op when one is already queued).</summary>
    public void RequestRedraw()
    {
        if (_redrawPending) return;
        _redrawPending = true;
        CallDeferred(MethodName.FlushRedraw);
    }

    private void FlushRedraw()
    {
        _redrawPending = false;
        if (Content != null)
            foreach (var child in Content.GetChildren())
            {
                Content.RemoveChild(child);
                child.QueueFree();
            }
        DrawContent();
    }

    /// <summary>Fills <see cref="Content"/>; called only from the deferred redraw.</summary>
    protected virtual void DrawContent() { }

    /// <summary>A wrapped label added to <see cref="Content"/>.</summary>
    protected Label AddText(string text, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        if (color is { } c) label.AddThemeColorOverride("font_color", c);
        Content!.AddChild(label);
        return label;
    }

    protected static readonly Color Red = new(1f, 0.45f, 0.45f);
    protected static readonly Color Green = new(0.55f, 0.9f, 0.55f);
    protected static readonly Color Amber = new(1f, 0.8f, 0.4f);
}
