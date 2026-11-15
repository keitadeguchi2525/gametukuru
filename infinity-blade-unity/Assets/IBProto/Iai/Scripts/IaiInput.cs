using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 居合バトルの入力（マウス＋キーボード）
public static class IaiInput
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
    public static bool LeftHeld
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            return Input.GetMouseButton(0);
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
    public static bool AutoToggle
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
    public static bool GuideToggle
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; return k != null && k.f2Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F2);
#endif
        }
    }
}
