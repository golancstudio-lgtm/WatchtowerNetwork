using TaleWorlds.Library;

namespace WatchtowerNetwork.WatchtowerSettlement.UI;

internal sealed class WatchtowerReportLineVM : ViewModel
{
    private readonly System.Action<string> _onLinkRequested;
    private string _text;

    [DataSourceProperty]
    public string Text
    {
        get => _text;
        set
        {
            if (value != _text)
            {
                _text = value;
                OnPropertyChangedWithValue(value, "Text");
            }
        }
    }

    public WatchtowerReportLineVM(string text, System.Action<string> onLinkRequested)
    {
        _text = text;
        _onLinkRequested = onLinkRequested;
    }

    public void ExecuteLink(string link)
    {
        _onLinkRequested(link);
    }
}
