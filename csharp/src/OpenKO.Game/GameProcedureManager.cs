using OpenKO.Common;

namespace OpenKO.Game;

/// <summary>
/// Drives the active <see cref="GameProcedure"/> and the transitions between procedures, faithfully
/// reproducing the static handover logic of the C++ <c>CGameProcedure</c>
/// (<c>ProcActiveSet</c>/<c>TickActive</c>/<c>RenderActive</c>).
///
/// The original switches state with a one-frame delay: <see cref="SetActive"/> only records the
/// pending procedure; the actual <see cref="GameProcedure.Release"/> of the old one and
/// <see cref="GameProcedure.Init"/> of the new one happen at the start of the next
/// <see cref="TickActive"/>. <see cref="RenderActive"/> draws only once the active procedure has been
/// initialised (i.e. <c>active == prev</c>), so a freshly-switched procedure is never rendered before
/// its <see cref="GameProcedure.Init"/> has run.
/// </summary>
public sealed class GameProcedureManager
{
    private readonly GameContext _context;
    private GameProcedure? _active;
    private GameProcedure? _previous;

    public GameProcedureManager(GameContext context) => _context = context;

    /// <summary>The procedure that is (or is about to become) active.</summary>
    public GameProcedure? Active => _active;

    /// <summary>The procedure tracked from the previous frame; equals <see cref="Active"/> once it is initialised.</summary>
    public GameProcedure? Previous => _previous;

    /// <summary>
    /// Request a switch to <paramref name="procedure"/> (port of <c>ProcActiveSet</c>). The handover is
    /// deferred to the next <see cref="TickActive"/>. No-ops if it is null or already active.
    /// </summary>
    public void SetActive(GameProcedure? procedure)
    {
        if (procedure == null || ReferenceEquals(_active, procedure))
            return;

        _previous = _active;
        _active = procedure;
    }

    /// <summary>
    /// Perform any pending handover, then tick the active procedure (port of <c>TickActive</c>).
    /// </summary>
    public void TickActive(float deltaSeconds)
    {
        if (!ReferenceEquals(_active, _previous))
        {
            _previous?.Release();
            if (_active != null)
            {
                _active.Bind(_context);
                _active.Init();
            }

            _previous = _active;
        }

        _active?.Tick(deltaSeconds);
    }

    /// <summary>Render the active procedure, but only once it has been initialised (port of <c>RenderActive</c>).</summary>
    public void RenderActive()
    {
        if (ReferenceEquals(_active, _previous))
            _active?.Render();
    }

    /// <summary>Dispatch a decoded packet to the active procedure.</summary>
    public bool DispatchPacket(Packet packet) => _active?.ProcessPacket(packet) ?? false;
}
