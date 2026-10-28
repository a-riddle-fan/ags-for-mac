using System;
using System.Runtime.InteropServices;

namespace AGS.Native
{
    /// <summary>Engine/editor constants from upstream native code (NativeMethods::GetNativeConstant).</summary>
    public static class NativeConstants
    {
        /// <summary>
        /// Returns the constant as <see cref="int"/>, <see cref="string"/> or <see cref="byte"/>[],
        /// or null for an unknown name — the same contract as the upstream editor's NativeProxy.
        /// </summary>
        public static object GetNativeConstant(string name)
        {
            if (name == null) return null;
            NativeLibraryInfo.EnsureCompatible();
            int type = NativeMethods.GetConstant(name, out int intValue, out IntPtr data, out int length);
            switch (type)
            {
                case NativeMethods.AGSN_CONST_INT:
                    return intValue;
                case NativeMethods.AGSN_CONST_STRING:
                    return Marshal.PtrToStringUTF8(data);
                case NativeMethods.AGSN_CONST_BYTES:
                    byte[] bytes = new byte[length];
                    Marshal.Copy(data, bytes, 0, length);
                    return bytes;
                default:
                    return null;
            }
        }
    }
}
