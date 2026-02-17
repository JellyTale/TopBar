using System;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace TopBar
{
    /// <summary>
    /// Undocumented COM interface used by Windows to set the default audio endpoint.
    /// This is the same mechanism the Sound control panel uses.
    /// </summary>
    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        // We only need SetDefaultEndpoint — pad the vtable with placeholders.
        [PreserveSig] int Unused1();
        [PreserveSig] int Unused2();
        [PreserveSig] int Unused3();
        [PreserveSig] int Unused4();
        [PreserveSig] int Unused5();
        [PreserveSig] int Unused6();
        [PreserveSig] int Unused7();
        [PreserveSig] int Unused8();
        [PreserveSig] int Unused9();
        [PreserveSig] int Unused10();

        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, Role role);
    }

    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class PolicyConfigClass { }

    /// <summary>
    /// Wrapper around the PolicyConfig COM object.
    /// </summary>
    internal class PolicyConfigClient
    {
        private readonly IPolicyConfig _policyConfig;

        public PolicyConfigClient()
        {
            _policyConfig = (IPolicyConfig)new PolicyConfigClass();
        }

        public void SetDefaultEndpoint(string deviceId, Role role)
        {
            Marshal.ThrowExceptionForHR(_policyConfig.SetDefaultEndpoint(deviceId, role));
        }
    }
}
