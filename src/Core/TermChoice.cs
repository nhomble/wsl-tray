namespace WslTray.Core
{
    class TermChoice
    {
        public string Id, Label;
        public TermChoice(string id, string label) { Id = id; Label = label; }
        public override string ToString() { return Label; }
    }
}
