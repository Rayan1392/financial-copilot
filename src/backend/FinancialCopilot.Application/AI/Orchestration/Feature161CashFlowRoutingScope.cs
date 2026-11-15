namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Feature 161 is opt-in and only active inside the V2 routing scope.</summary>
public sealed record Feature161CashFlowRoutingScope(bool Enabled, Feature154RoutingScope V2Scope)
{
    public bool IsActive => Enabled && V2Scope.IsActive;
}
