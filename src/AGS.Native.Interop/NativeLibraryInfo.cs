using System;
using System.Runtime.InteropServices;

namespace AGS.Native
{
    public static class NativeLibraryInfo
    {
        /// <summary>The C API version this assembly was written against (AGSN_API_VERSION).</summary>
        public const int ExpectedApiVersion = 1;

        /// <summary>
        /// Checks that libagsnative can be loaded and matches this assembly. Throws a descriptive
        /// exception otherwise, so a missing or stale dylib is reported at startup, not on first use.
        /// </summary>
        public static void EnsureCompatible()
        {
            int version;
            try
            {
                version = NativeMethods.ApiVersion();
            }
            catch (DllNotFoundException ex)
            {
                throw new DllNotFoundException("libagsnative.dylib could not be loaded; build it with scripts/build-native.sh.", ex);
            }
            if (version != ExpectedApiVersion)
                throw new InvalidOperationException($"libagsnative API version {version} does not match the expected version {ExpectedApiVersion}; rebuild it.");
            NativeMethods.Initialize();
        }

        /// <summary>Version of the upstream AGS code the native library was built from, e.g. "3.6.4.0".</summary>
        public static string AgsVersion => Marshal.PtrToStringUTF8(NativeMethods.AgsVersion());
    }
}
