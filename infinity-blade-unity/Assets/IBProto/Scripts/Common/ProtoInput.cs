using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 旧Input Manager／新Input System のどちらでも動く入力まとめ
public static class ProtoInput
{
    public static Vector2 MousePos
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }
    }

    public static bool LeftPressed
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }
    }

    public static bool LeftReleased
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp(0);
#endif
        }
    }

    public static bool RightHeld
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
            return Input.GetMouseButton(1);
#endif
        }
    }

    public static bool DodgeLeft
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; return k != null && (k.aKey.wasPressedThisFrame || k.leftArrowKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
#endif
        }
    }

    public static bool DodgeRight
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; return k != null && (k.dKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);
#endif
        }
    }

    public static bool GuardHeld
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            bool key = k != null && (k.sKey.isPressed || k.downArrowKey.isPressed || k.leftShiftKey.isPressed);
            return key || RightHeld;
#else
            return Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.LeftShift) || RightHeld;
#endif
        }
    }

    public static bool DebugToggle
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; return k != null && k.f1Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F1);
#endif
        }
    }

    // ホイール量（上でプラス）
    public static float Scroll
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
#else
            return Input.mouseScrollDelta.y * 120f;
#endif
        }
    }

    static bool KeyDown(
#if ENABLE_INPUT_SYSTEM
        System.Func<Keyboard, bool> f
#else
        params KeyCode[] codes
#endif
    )
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current; return k != null && f(k);
#else
        foreach (var c in codes) if (Input.GetKeyDown(c)) return true; return false;
#endif
    }

    // 右クリック（押した瞬間）：気刃斬り
    public static bool RightPressed
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }
    }

#if ENABLE_INPUT_SYSTEM
    // S：斬り下がり
    public static bool FadePressed => KeyDown(k => k.sKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame);
    // Space：見切り斬り
    public static bool CounterPressed => KeyDown(k => k.spaceKey.wasPressedThisFrame);
    // E：気刃兜割り（ウルト）
    public static bool UltPressed => KeyDown(k => k.eKey.wasPressedThisFrame || k.qKey.wasPressedThisFrame);
    // F9：録画の開始／停止
    public static bool RecordToggle => KeyDown(k => k.f9Key.wasPressedThisFrame);
#else
    public static bool RecordToggle => KeyDown(KeyCode.F9);
    public static bool FadePressed => KeyDown(KeyCode.S, KeyCode.DownArrow);
    public static bool CounterPressed => KeyDown(KeyCode.Space);
    public static bool UltPressed => KeyDown(KeyCode.E, KeyCode.Q);
#endif
}
