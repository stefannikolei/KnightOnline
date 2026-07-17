namespace OpenKO.Client.Game.Ui;

/// <summary>
/// e_PerTradeState (Client/WarFare/SubProcPerTrade.h) — the player-to-player trade state machine.
/// Values match the C++ enum ordering verbatim so the transitions read the same.
/// </summary>
public enum PerTradeState
{
    /// <summary>PER_TRADE_STATE_NONE — no trade in progress.</summary>
    None = 0,

    /// <summary>PER_TRADE_STATE_WAIT_FOR_REQ — I asked; waiting for the target to accept/reject.</summary>
    WaitForReq = 1,

    /// <summary>
    /// PER_TRADE_STATE_WAIT_FOR_MY_DECISION_AGREE_OR_DISAGREE — I was asked; the permit
    /// yes/no box is up.
    /// </summary>
    WaitForMyDecision = 2,

    /// <summary>PER_TRADE_STATE_NORMAL — both accepted; the trade window is live.</summary>
    Normal = 3,

    /// <summary>PER_TRADE_STATE_ADD_AND_WAIT_FROM_SERVER — an ADD is in flight (unused as a resting state).</summary>
    AddAndWaitFromServer = 4,

    /// <summary>PER_TRARE_STATE_EDITTING — the gold/count popup owns input.</summary>
    Editting = 5,

    /// <summary>PER_TRADE_STATE_MY_TRADE_DECISION_DONE — I pressed my ready button; my icons are frozen.</summary>
    MyTradeDecisionDone = 6,
}
