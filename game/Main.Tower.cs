using Dialoid.Game.Ui.Tower;
using Godot;

namespace Dialoid.Game;

public partial class Main
{
    public TowerBoardView? TowerBoard { get; private set; }
    public bool IsTowerOpen => TowerBoard?.Visible == true;
    private Control? _towerReturnFocus;

    private void BuildTowerBoard()
    {
        var board = TowerBoard = new TowerBoardView { Name = "TowerBoard", Visible = false };
        AddChild(board);
        board.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        board.Setup(DataDirectory!);
        board.CloseRequested += CloseTowerBoard;
        Bar!.AddTowerButton(ToggleTowerBoard);
        if (OS.GetCmdlineUserArgs().Contains("--tower-board")) OpenTowerBoard();
    }

    public void ToggleTowerBoard()
    {
        if (IsTowerOpen) CloseTowerBoard(); else OpenTowerBoard();
    }

    public void OpenTowerBoard()
    {
        if (TowerBoard == null || IsTowerOpen) return;
        _towerReturnFocus = GetViewport().GuiGetFocusOwner();
        ClearHeldInput();
        Windows.ProcessMode = ProcessModeEnum.Disabled;
        TowerBoard.Show();
        MoveChild(TowerBoard, GetChildCount() - 1);
        TowerBoard.FindNextValidFocus()?.GrabFocus();
    }

    public void CloseTowerBoard()
    {
        if (!IsTowerOpen) return;
        TowerBoard!.Hide();
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        Windows.ProcessMode = ProcessModeEnum.Inherit;
        Windows.RefreshInputDevice();
        ClearHeldInput();
        if (GodotObject.IsInstanceValid(_towerReturnFocus) && _towerReturnFocus!.IsVisibleInTree() &&
            _towerReturnFocus.FocusMode != Control.FocusModeEnum.None)
            _towerReturnFocus.GrabFocus();
    }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.PhysicalKeycode == Key.F6 || key.Keycode == Key.F6)
            {
                ToggleTowerBoard();
                GetViewport().SetInputAsHandled();
            }
            else if (IsTowerOpen && (key.PhysicalKeycode == Key.Escape || key.Keycode == Key.Escape))
            {
                CloseTowerBoard();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (IsTowerOpen && input is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B })
        {
            CloseTowerBoard();
            GetViewport().SetInputAsHandled();
        }
        else if (IsTowerOpen && input is InputEventJoypadButton { ButtonIndex: JoyButton.RightStick })
        {
            // Camera reset listens to R3 even while its analog input is blocked.
            GetViewport().SetInputAsHandled();
        }
    }
}
