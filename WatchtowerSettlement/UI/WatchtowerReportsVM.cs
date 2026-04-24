using System;
using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace WatchtowerNetwork.WatchtowerSettlement.UI;

internal sealed class WatchtowerReportsVM : ViewModel
{
    private readonly Action _onCloseRequested;
    private readonly Action<string> _onLinkRequested;
    private string _title = string.Empty;
    private string _emptyText = string.Empty;
    private bool _hasReports;
    private MBBindingList<WatchtowerReportLineVM> _reports;

    [DataSourceProperty]
    public string Title
    {
        get => _title;
        set
        {
            if (value != _title)
            {
                _title = value;
                OnPropertyChangedWithValue(value, "Title");
            }
        }
    }

    [DataSourceProperty]
    public string EmptyText
    {
        get => _emptyText;
        set
        {
            if (value != _emptyText)
            {
                _emptyText = value;
                OnPropertyChangedWithValue(value, "EmptyText");
            }
        }
    }

    [DataSourceProperty]
    public bool HasReports
    {
        get => _hasReports;
        set
        {
            if (value != _hasReports)
            {
                _hasReports = value;
                OnPropertyChangedWithValue(value, "HasReports");
                OnPropertyChangedWithValue(!value, "HasNoReports");
            }
        }
    }

    [DataSourceProperty]
    public bool HasNoReports => !_hasReports;

    [DataSourceProperty]
    public MBBindingList<WatchtowerReportLineVM> Reports
    {
        get => _reports;
        set
        {
            if (value != _reports)
            {
                _reports = value;
                OnPropertyChangedWithValue(value, "Reports");
            }
        }
    }

    public WatchtowerReportsVM(Action onCloseRequested, Action<string> onLinkRequested)
    {
        _onCloseRequested = onCloseRequested;
        _onLinkRequested = onLinkRequested;
        _reports = new MBBindingList<WatchtowerReportLineVM>();
        RefreshValues();
    }

    public override void RefreshValues()
    {
        base.RefreshValues();
        Title = new TextObject("{=0VnYu0si0}Watchtower Reports").ToString();
        EmptyText = new TextObject("{=hIKjqGFeR}No reports are currently available.").ToString();
    }

    public void SetReports(IEnumerable<TextObject> reportTexts)
    {
        Reports.Clear();
        foreach (TextObject reportText in reportTexts)
        {
            Reports.Add(new WatchtowerReportLineVM(reportText.ToString(), _onLinkRequested));
        }

        HasReports = Reports.Count > 0;
    }

    public void ExecuteClose()
    {
        _onCloseRequested();
    }

    public override void OnFinalize()
    {
        base.OnFinalize();
        Reports.Clear();
    }
}
