using System;
using System.Diagnostics;

namespace WatchtowerNetwork.Heatmaps;

internal static class LoadingProcessHintTracker
{
    public static event Action? Changed;

    private static readonly object Sync = new object();
    private static readonly Stopwatch TaskTimer = new Stopwatch();
    private static string? _currentTask;
    private static int _currentStep;
    private static int _totalSteps;

    public static void BeginTask(string taskName, int totalSteps)
    {
        lock (Sync)
        {
            _currentTask = taskName;
            _totalSteps = Math.Max(1, totalSteps);
            _currentStep = 0;
            TaskTimer.Restart();
            ResetLast();
        }

        Changed?.Invoke();
    }

    public static void ReportProgress(int completedSteps)
    {
        lock (Sync)
        {
            if (_currentTask == null)
            {
                return;
            }

            if (completedSteps <= 0)
            {
                _currentStep = 0;
            }
            else if (completedSteps >= _totalSteps)
            {
                _currentStep = _totalSteps;
            }
            else
            {
                _currentStep = completedSteps;
            }
        }

        Changed?.Invoke();
    }

    public static void CompleteTask()
    {
        lock (Sync)
        {
            if (_currentTask == null)
            {
                return;
            }

            _currentStep = _totalSteps;
        }

        Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (Sync)
        {
            _currentTask = null;
            _currentStep = 0;
            _totalSteps = 0;
            TaskTimer.Reset();
            ResetLast();
        }

        Changed?.Invoke();
    }

    public static bool TryBuildHint(out string title, out string description)
    {
        lock (Sync)
        {
            if (string.IsNullOrWhiteSpace(_currentTask))
            {
                title = string.Empty;
                description = string.Empty;
                return false;
            }

            int progressPercent = (_currentStep * 100) / Math.Max(1, _totalSteps);
            string etaText = BuildEtaText(progressPercent);

            title = _currentTask!;
            description = $"Progress: {progressPercent}% ({_currentStep} from {_totalSteps})\nETA: {etaText}";
            return true;
        }
    }

    public static bool TryGetSnapshot(out string taskName, out int progressPercent, out string etaText)
    {
        lock (Sync)
        {
            if (string.IsNullOrWhiteSpace(_currentTask))
            {
                taskName = "NONE";
                progressPercent = 0;
                etaText = "∞";
                return false;
            }

            progressPercent = (_currentStep * 100) / Math.Max(1, _totalSteps);
            taskName = _currentTask!;
            etaText = BuildEtaText(progressPercent);
            return true;
        }
    }

    private static int lastSeconds = 0;
    private static int lastProgressPercent = 0;

    private static string BuildEtaText(int progressPercent)
    {
        if (!TaskTimer.IsRunning || progressPercent <= 0)
        {
            return "--:--";
        }
        double remainingSeconds;
        if (progressPercent == lastProgressPercent)
        {
            remainingSeconds = lastSeconds;
        }
        else
        {
            double estimatedTotalSeconds = TaskTimer.Elapsed.TotalSeconds * 100d / progressPercent;
            remainingSeconds = Math.Max(0d, estimatedTotalSeconds - TaskTimer.Elapsed.TotalSeconds);
            lastSeconds = (int)remainingSeconds;
            lastProgressPercent = progressPercent;
        }

        TimeSpan remaining = TimeSpan.FromSeconds(remainingSeconds);
        return $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
    }

    private static void ResetLast()
    {
        lastSeconds = 0;
        lastProgressPercent = 0;
    }
}
