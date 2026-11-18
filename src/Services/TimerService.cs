// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Windows.Threading;

namespace DIsland.Services;

public sealed class TimerService
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DateTime _endUtc;
    private TimeSpan _pausedRemaining;

    public TimerService()
    {
        _tick.Tick += (_, _) => OnTick();
    }

    public bool IsActive { get; private set; }
    public bool IsPaused { get; private set; }
    public TimeSpan TotalDuration { get; private set; }

    public TimeSpan Remaining =>
        !IsActive ? TimeSpan.Zero
        : IsPaused ? _pausedRemaining
        : Max(TimeSpan.Zero, _endUtc - DateTime.UtcNow);

    /// <summary>Remaining fraction 0..1 for perimeter focus border and circular activity rings.</summary>
    public double RemainingRatio =>
        !IsActive || TotalDuration.Ticks <= 0
            ? 0
            : Math.Clamp(Remaining.TotalSeconds / TotalDuration.TotalSeconds, 0, 1);

    /// <summary>Remaining time changed (fires a few times per second while running).</summary>
    public event Action? Tick;
    /// <summary>Started, paused, resumed, cancelled or finished.</summary>
    public event Action? StateChanged;
    public event Action? Finished;

    public void Start(TimeSpan duration)
    {
        TotalDuration = Max(TimeSpan.FromSeconds(1), duration);
        _endUtc = DateTime.UtcNow + duration;
        IsActive = true;
        IsPaused = false;
        _tick.Start();
        StateChanged?.Invoke();
        Tick?.Invoke();
    }

    public void TogglePause()
    {
        if (!IsActive) return;
        if (IsPaused)
        {
            _endUtc = DateTime.UtcNow + _pausedRemaining;
            IsPaused = false;
            _tick.Start();
        }
        else
        {
            _pausedRemaining = Remaining;
            IsPaused = true;
            _tick.Stop();
        }
        StateChanged?.Invoke();
    }

    public void Add(TimeSpan amount)
    {
        if (!IsActive) return;
        if (IsPaused) _pausedRemaining += amount;
        else _endUtc += amount;
        if (Remaining > TotalDuration) TotalDuration = Remaining;
        Tick?.Invoke();
    }

    public void Cancel()
    {
        if (!IsActive) return;
        IsActive = false;
        IsPaused = false;
        TotalDuration = TimeSpan.Zero;
        _tick.Stop();
        StateChanged?.Invoke();
    }

    private void OnTick()
    {
        if (Remaining > TimeSpan.Zero)
        {
            Tick?.Invoke();
            return;
        }
        IsActive = false;
        _tick.Stop();
        StateChanged?.Invoke();
        Finished?.Invoke();
    }

    public static string Format(TimeSpan t)
    {
        var total = (int)Math.Ceiling(t.TotalSeconds);
        var h = total / 3600;
        var m = total / 60 % 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }

    public static string FormatCompact(TimeSpan t)
    {
        var total = Math.Max(0, (int)Math.Ceiling(t.TotalSeconds));
        if (total >= 3600) return $"{total / 3600}h";
        if (total >= 60) return $"{(int)Math.Ceiling(total / 60.0)}m";
        return $"{total}s";
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
