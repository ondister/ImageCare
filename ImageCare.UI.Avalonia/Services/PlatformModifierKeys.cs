using System;
using System.Runtime.InteropServices;

using Avalonia.Input;

namespace ImageCare.UI.Avalonia.Services;

internal static class PlatformModifierKeys
{
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private const uint HidSystemState = 1;
    private const ulong ShiftMask = 1UL << 17;
    private const ulong ControlMask = 1UL << 18;
    private const ulong OptionMask = 1UL << 19;
    private const ulong CommandMask = 1UL << 20;

    public static KeyModifiers GetCurrent(KeyModifiers fallbackModifiers) =>
        TryGetCurrent(out var modifiers) ? modifiers : fallbackModifiers;

    public static bool TryGetCurrent(out KeyModifiers modifiers)
    {
        modifiers = KeyModifiers.None;
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            var flags = CGEventSourceFlagsState(HidSystemState);
            if ((flags & ControlMask) != 0)
            {
                modifiers |= KeyModifiers.Control;
            }

            if ((flags & OptionMask) != 0)
            {
                modifiers |= KeyModifiers.Alt;
            }

            if ((flags & ShiftMask) != 0)
            {
                modifiers |= KeyModifiers.Shift;
            }

            if ((flags & CommandMask) != 0)
            {
                modifiers |= KeyModifiers.Meta;
            }

            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport(ApplicationServices)]
    private static extern ulong CGEventSourceFlagsState(uint stateId);
}
