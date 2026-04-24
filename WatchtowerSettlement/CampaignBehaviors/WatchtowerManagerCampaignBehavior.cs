using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using static WatchtowerNetwork.WatchtowerSettlement.WatchtowerSettlementComponent;

namespace WatchtowerNetwork.WatchtowerSettlement.CampaignBehaviors;

internal class WatchtowerManagerCampaignBehavior : CampaignBehaviorBase
{
    private const string SaveKey = "watchtowers_reports_data";
    private const string SettlementSeparator = "::";
    private const char ReportSeparator = ',';
    private const char ReportPartSeparator = '|';
    private Dictionary<WatchtowerSettlementComponent, List<WatchtowerSettlementComponent.WatchtowerReport>> _cachedReports = new();

    public override void RegisterEvents()
    {
        CampaignEvents.HourlyTickSettlementEvent.AddNonSerializedListener(this, OnHourlyTickSettlement);
        CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
        CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, OnGameLoadFinished);
    }

    private void OnGameLoadFinished()
    {
        if (_cachedReports.Count == 0)
        {
            return;
        }

        SyncWatchtowers();
        foreach (var watchtower in _cachedReports.Keys)
        {
            watchtower.ValidateReports();
        }
    }

    private void OnNewGameCreated(CampaignGameStarter starter)
    {
        _cachedReports.Clear();
    }

    private void OnHourlyTickSettlement(Settlement settlement)
    {
        if (settlement is not null && settlement.TryGetWatchtower(out WatchtowerSettlementComponent? watchtower) && watchtower is not null)
        {
            watchtower.HourlyTick();
            SyncCache(watchtower);
        }
    }

    public override void SyncData(IDataStore dataStore)
    {
        List<string> dataToSave = new List<string>();
        if (dataStore.IsSaving)
        {
            UpdateAllWatchtowerCaches();
            dataToSave = BuildSaveData();
        }

        dataStore.SyncData(SaveKey, ref dataToSave);

        if (dataStore.IsLoading)
        {
            LoadSaveData(dataToSave);
        }
    }

    private List<string> BuildSaveData()
    {
        List<string> data = new();
        foreach (KeyValuePair<WatchtowerSettlementComponent, List<WatchtowerReport>> cachedReport in _cachedReports)
        {
            string watchtowerSettlementId = cachedReport.Key?.Settlement?.StringId ?? string.Empty;
            if (watchtowerSettlementId.Length == 0)
            {
                continue;
            }

            HashSet<string> reportEntries = new();
            foreach (WatchtowerReport report in cachedReport.Value)
            {
                string partyId = report.Party?.StringId ?? string.Empty;
                if (partyId.Length == 0)
                {
                    continue;
                }

                reportEntries.Add(string.Join(ReportPartSeparator.ToString(),
                    partyId,
                    report.SoldiersCount.ToString(CultureInfo.InvariantCulture),
                    report.PrisonersCount.ToString(CultureInfo.InvariantCulture),
                    report.LastUpdateTime.ToHours.ToString("R", CultureInfo.InvariantCulture)));
            }

            if (reportEntries.Count > 0)
            {
                data.Add($"{watchtowerSettlementId}{SettlementSeparator}{string.Join(ReportSeparator.ToString(), reportEntries)}");
            }
        }

        return data;
    }

    private void LoadSaveData(List<string> saveData)
    {
        _cachedReports.Clear();

        foreach (string data in saveData)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                continue;
            }

            string[] splitData = data.Split(new[] { SettlementSeparator }, 2, StringSplitOptions.None);
            if (splitData.Length != 2 || string.IsNullOrWhiteSpace(splitData[0]) || string.IsNullOrWhiteSpace(splitData[1]))
            {
                continue;
            }

            Settlement watchtowerSettlement = Settlement.Find(splitData[0]);
            if (watchtowerSettlement is null || !watchtowerSettlement.TryGetWatchtower(out WatchtowerSettlementComponent? watchtower) || watchtower is null)
            {
                continue;
            }

            List<WatchtowerReport> reports = new();
            string[] reportEntries = splitData[1].Split(new[] { ReportSeparator }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string reportEntry in reportEntries)
            {
                string[] reportParts = reportEntry.Split(new[] { ReportPartSeparator }, StringSplitOptions.None);
                if (reportParts.Length != 4 || string.IsNullOrWhiteSpace(reportParts[0]))
                {
                    continue;
                }

                MobileParty? party = MobileParty.All.FirstOrDefault(p => p.StringId == reportParts[0]);
                if (party is null
                    || !int.TryParse(reportParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int savedSoldiersCount)
                    || !int.TryParse(reportParts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int savedPrisonersCount)
                    || !double.TryParse(reportParts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double savedLastUpdateHours))
                {
                    continue;
                }

                if (reports.Any(r => r.Party == party))
                {
                    continue;
                }

                WatchtowerReport report = new WatchtowerReport(party);
                report.ApplySavedSnapshot(savedSoldiersCount, savedPrisonersCount, CampaignTime.Hours((float)savedLastUpdateHours));
                reports.Add(report);
            }

            if (reports.Count > 0)
            {
                _cachedReports[watchtower] = reports;
            }
        }
    }

    private void UpdateAllWatchtowerCaches()
    {
        foreach (Settlement settlement in Settlement.All)
        {
            if (settlement.TryGetWatchtower(out WatchtowerSettlementComponent? watchtower) && watchtower is not null)
            {
                SyncCache(watchtower);
            }
        }
    }

    private void SyncCache(WatchtowerSettlementComponent watchtower)
    {
        if (!_cachedReports.TryGetValue(watchtower, out List<WatchtowerReport>? reports))
        {
            reports = new List<WatchtowerReport>();
            _cachedReports[watchtower] = reports;
        }

        foreach (var report in watchtower.GetCurrentReport())
        {
            if (report.IsValid && !reports.Any(wr => wr.Party == report.Party))
            {
                reports.Add(report);
            }
        }
    }

    private void SyncWatchtowers()
    {
        foreach (KeyValuePair<WatchtowerSettlementComponent, List<WatchtowerReport>> cached in _cachedReports)
        {
            WatchtowerSettlementComponent watchtower = cached.Key;
            foreach (WatchtowerReport report in cached.Value)
            {
                watchtower.TryAddReport(report);
            }
        }
    }
}
