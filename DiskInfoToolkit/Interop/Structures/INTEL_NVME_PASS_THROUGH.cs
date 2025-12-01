using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Interop.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct INTEL_NVME_PASS_THROUGH
    {
        public INTEL_NVME_PASS_THROUGH()
        {
            SRB = new();
            Payload = new();

            DataBuffer = new byte[0x1000];
        }

        public SRB_IO_CONTROL SRB;     // 0x0000 ~ 0x001B
        public INTEL_NVME_PAYLOAD Payload;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4096)]
        public byte[] DataBuffer;
    }
}
