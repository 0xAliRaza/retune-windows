using Retune.App.Interop;
using Retune.Presentation.Player;

namespace Retune.App.Services;

/// <summary>Stops the display and PC sleeping while video plays (UI thread, which owns the execution state).</summary>
public sealed class KeepAwake : IKeepAwake
{
    private bool _awake;

    void IKeepAwake.KeepAwake(bool awake)
    {
        if (awake == _awake)
        {
            return;
        }

        _awake = awake;
        _ = NativeMethods.SetThreadExecutionState(awake
            ? NativeMethods.EsContinuous | NativeMethods.EsDisplayRequired | NativeMethods.EsSystemRequired
            : NativeMethods.EsContinuous);
    }
}
