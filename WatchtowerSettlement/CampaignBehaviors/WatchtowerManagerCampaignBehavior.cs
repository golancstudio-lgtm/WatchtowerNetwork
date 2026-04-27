using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace WatchtowerNetwork.WatchtowerSettlement.CampaignBehaviors;

internal class WatchtowerManagerCampaignBehavior : CampaignBehaviorBase
{
    private const string SaveKey = "watchtowers_reports_data";
    private Dictionary<WatchtowerSettlementComponent, List<WatchtowerReport>> _cachedReports = new();

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

    private void SyncCache(WatchtowerSettlementComponent watchtower)
    {
        _cachedReports[watchtower] = watchtower.GetWatchtowerReports().ToList();
    }

    public override void SyncData(IDataStore dataStore)
    {
        List<string> dataToSave = new List<string>();
        if (dataStore.IsSaving)
        {
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
        /**
         * : - between WatchtowerReport properties
         * | - between different WatchtowerReports of the same watchtower
         * # - between watchtowerId and all the WatchtowerReports of this watchtower
         * Report text is base64-encoded so links and localized text cannot collide with delimiters.
         **/
        List<string> data = new();
        foreach (KeyValuePair<WatchtowerSettlementComponent, List<WatchtowerReport>> cachedReport in _cachedReports)
        {
            string watchtowerId = cachedReport.Key.StringId;
            List<string> entries = new List<string>();
            foreach (WatchtowerReport report in cachedReport.Value)
            {
                string reportString =
                    report.Party.StringId + ":" +
                    report.IsArmy.ToString() + ":" +
                    report.SoldiersCount.ToString() + ":" +
                    report.PrisonersCount.ToString() + ":" +
                    report.IsSevereThreat.ToString() + ":" +
                    report.LastUpdateTime.ToSeconds.ToString(CultureInfo.InvariantCulture) + ":" +
                    EncodeReportText(report.TextReport);
                entries.Add(reportString);
            }
            string joinedEntries = string.Join("|", entries);
            data.Add(watchtowerId + "#" + joinedEntries);
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
            string[] splitStage1 = data.Split('#');
            if (splitStage1.Length != 2)
            {
                continue;
            }
            string
                watchtowerId = splitStage1[0],
                joinedReports = splitStage1[1];
            if (string.IsNullOrWhiteSpace(watchtowerId) || string.IsNullOrWhiteSpace(joinedReports))
            {
                continue;
            }
            WatchtowerSettlementComponent watchtower = MBObjectManager.Instance.GetObject<WatchtowerSettlementComponent>(watchtowerId);
            if (watchtower is null)
            {
                continue;
            }
            string[] splitStage2 = joinedReports.Split('|');
            List<WatchtowerReport> watchtowerReports = new List<WatchtowerReport>();
            foreach (string reportLine in splitStage2)
            {
                if (string.IsNullOrEmpty(reportLine))
                {
                    continue;
                }
                string[] splitStage3 = reportLine.Split(':');
                if (splitStage3.Length != 7)
                {
                    continue;
                }

                MobileParty? party = MobileParty.All.Find(p => p.StringId == splitStage3[0]);
                if (party is null)
                {
                    continue;
                }
                if (!bool.TryParse(splitStage3[1], out bool isArmy))
                {
                    continue;
                }
                if (!int.TryParse(splitStage3[2], out int soldiersCount))
                {
                    continue;
                }
                if (!int.TryParse(splitStage3[3], out int prisonersCount))
                {
                    continue;
                }
                if (!bool.TryParse(splitStage3[4], out bool isSevereThreat))
                {
                    continue;
                }
                if (!double.TryParse(splitStage3[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double lastUpdateTimeSeconds))
                {
                    continue;
                }
                string? textReport = DecodeReportText(splitStage3[6]);
                CampaignTime lastUpdateTime = CampaignTime.Seconds((long)lastUpdateTimeSeconds);
                WatchtowerReport watchtowerReport = new WatchtowerReport() { MapFaction = watchtower.MapFaction };
                watchtowerReport.ApplySavedSnapshot(
                    party,
                    isArmy,
                    soldiersCount,
                    prisonersCount,
                    isSevereThreat,
                    lastUpdateTime,
                    textReport);
                if (watchtowerReport.IsValid)
                {
                    watchtowerReports.Add(watchtowerReport);
                }
            }
            _cachedReports.Add(watchtower, watchtowerReports);
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

    private static string EncodeReportText(TextObject? textReport)
    {
        string text = textReport?.ToString() ?? string.Empty;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    }

    private static string? DecodeReportText(string encodedText)
    {
        if (string.IsNullOrWhiteSpace(encodedText))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encodedText));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
