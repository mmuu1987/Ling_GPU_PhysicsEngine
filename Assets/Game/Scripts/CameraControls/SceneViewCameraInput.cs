using UnityEngine;

public struct SceneViewCameraInput
{
    public bool CanUseInput;
    public bool AltPressed;
    public bool ShiftPressed;
    public bool FocusPressed;

    public bool LeftDown;
    public bool LeftHeld;
    public bool LeftUp;
    public bool RightDown;
    public bool RightHeld;
    public bool RightUp;
    public bool MiddleDown;
    public bool MiddleHeld;
    public bool MiddleUp;

    public bool MoveForward;
    public bool MoveBackward;
    public bool MoveLeft;
    public bool MoveRight;
    public bool MoveUp;
    public bool MoveDown;

    public float MouseWheel;
    public Vector3 MousePosition;

    public bool AnyMouseButtonUp => LeftUp || RightUp || MiddleUp;
}

public static class SceneViewCameraInputReader
{
    public static SceneViewCameraInput Read(SceneViewCameraSettings settings)
    {
        return new SceneViewCameraInput
        {
            CanUseInput = IsMouseInsideInputArea(settings),
            AltPressed = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr),
            ShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
            FocusPressed = Input.GetKeyDown(KeyCode.F),
            LeftDown = Input.GetMouseButtonDown(0),
            LeftHeld = Input.GetMouseButton(0),
            LeftUp = Input.GetMouseButtonUp(0),
            RightDown = Input.GetMouseButtonDown(1),
            RightHeld = Input.GetMouseButton(1),
            RightUp = Input.GetMouseButtonUp(1),
            MiddleDown = Input.GetMouseButtonDown(2),
            MiddleHeld = Input.GetMouseButton(2),
            MiddleUp = Input.GetMouseButtonUp(2),
            MoveForward = Input.GetKey(KeyCode.W),
            MoveBackward = Input.GetKey(KeyCode.S),
            MoveLeft = Input.GetKey(KeyCode.A),
            MoveRight = Input.GetKey(KeyCode.D),
            MoveUp = Input.GetKey(KeyCode.E),
            MoveDown = Input.GetKey(KeyCode.Q),
            MouseWheel = Input.GetAxis("Mouse ScrollWheel"),
            MousePosition = Input.mousePosition
        };
    }

    private static bool IsMouseInsideInputArea(SceneViewCameraSettings settings)
    {
        if (!settings.RequireMouseInsideScreen)
            return true;

        Vector3 mouse = Input.mousePosition;
        Rect normalized = settings.NormalizedInputArea;
        Rect area = new Rect(
            normalized.x * Screen.width,
            normalized.y * Screen.height,
            normalized.width * Screen.width,
            normalized.height * Screen.height);

        return area.Contains(mouse);
    }
}

