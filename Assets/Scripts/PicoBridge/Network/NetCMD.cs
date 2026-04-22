namespace PicoBridge.Network
{
    /// <summary>
    /// Protocol constants — matches XRobo NetCMD.
    /// </summary>
    public static class NetCMD
    {
        // Direction markers
        public const byte HEAD_VR_TO_PC = 0x3F;
        public const byte HEAD_PC_TO_VR = 0xCF;
        public const byte END_BYTE = 0xA5;

        // Minimum packet size: HEAD(1) + CMD(1) + LEN(4) + TIMESTAMP(8) + END(1)
        public const int DEFAULT_PACKAGE_SIZE = 15;
        public const int DEFAULT_TCP_PORT = 63901;
        public const int MAX_PAYLOAD_SIZE = 4 * 1024 * 1024;

        // VR -> PC commands
        public const byte PACKET_CCMD_CONNECT = 0x19;
        public const byte PACKET_CCMD_SEND_VERSION = 0x6C;
        public const byte PACKET_CCMD_TO_CONTROLLER_FUNCTION = 0x6D;
        public const byte PACKET_CCMD_CLIENT_HEARTBEAT = 0x23;
        public const byte PACKET_CMD_CUSTOM_TO_PC = 0x72;

        // PC -> VR commands
        public const byte PACKET_CMD_FROM_CONTROLLER_COMMON_FUNCTION = 0x5F;
        public const byte PACKET_CMD_CUSTOM_TO_VR = 0x71;
        public const byte PACKET_CMD_TCPIP = 0x7E;
        public const byte PACKET_CMD_MEDIAIP = 0x7F;
    }
}
