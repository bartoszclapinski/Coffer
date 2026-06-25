namespace Coffer.Core.Goals;

/// <summary>
/// The kinds of savings goal the advisor supports (doc 07). Each maps to one
/// <see cref="GoalStrategy"/>. Persisted as the enum name so the column stays readable.
/// </summary>
public enum GoalType
{
    /// <summary>A one-time defined amount with a target date (furniture, phone, equipment).</summary>
    Purchase,

    /// <summary>A larger one-off that benefits from seasonality awareness (vacation, renovation, car).</summary>
    LargeExpense,

    /// <summary>A multiple of monthly expenses (default 6×); target grows with expenses.</summary>
    EmergencyFund,

    /// <summary>Overpay home-loan principal (shorten duration or reduce payment).</summary>
    MortgagePrepayment,

    /// <summary>Earmark free cash for investing — no instrument advice, no return prediction.</summary>
    Investment,

    /// <summary>A 5+ year horizon with inflation modelling (retirement, child education).</summary>
    LongTerm,
}
