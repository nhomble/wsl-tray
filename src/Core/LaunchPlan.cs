namespace WslTray.Core
{
    class LaunchPlan
    {
        public string Exe, Args;
        public string ResolvedId;   // preset actually used after fallback
        public string Warning;      // non-null: fell back to Console; show to the user
        public string Error;        // non-null: do not launch
    }
}
