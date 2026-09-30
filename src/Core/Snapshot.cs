using System.Collections.Generic;

namespace WslTray.Core
{
    class Snapshot
    {
        public List<Distro> Distros = new List<Distro>();
        public string DefaultName;
        public bool VmPresent;
        public long VmRamBytes;
        public List<string> Running = new List<string>(); // null = unknown (wsl call failed/timed out)
        public string WslCall = "skipped";                // skipped | ok | timeout | error
        public long WslCallMs;
    }
}
