namespace WslTray.Core
{
    class Distro
    {
        public string Guid, Name, BasePath, VhdFileName;
        public int Version;
        public bool IsDefault;
        public long VhdxBytes = -1;
        public string VhdxPath;
    }
}
