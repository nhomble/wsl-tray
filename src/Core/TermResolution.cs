namespace WslTray.Core
{
    class TermResolution
    {
        public string Id;
        public bool Write;          // value must be (re)written to the registry
        public string StaleLabel;   // non-null: the user's chosen terminal was not found; Id is what was used instead
    }
}
