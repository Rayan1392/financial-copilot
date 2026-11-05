namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Feature 154 routing applies only to V2 orchestration (POLICY-V1-FREEZE). The shared semantic infrastructure
/// (interpreter, hybrid interpreter, dialogue gate, enricher, statement-table executor) also serves the frozen V1
/// path, so each consults <see cref="IsActive"/> at call time: when it is false every shared component behaves
/// exactly as before Feature 154.
/// <list type="bullet">
/// <item><see cref="Enabled"/> comes from the existing <c>AiOrchestration:Mode</c> (no new configuration key).</item>
/// <item>The frozen V1 service also marks its own execution with <see cref="EnterFrozenV1"/>, so an emergency
/// rollback that only swaps the orchestration service still keeps V1 unchanged.</item>
/// </list>
/// </summary>
public sealed record Feature154RoutingScope(bool Enabled)
{
    public static readonly Feature154RoutingScope V2 = new(true);
    public static readonly Feature154RoutingScope V1Frozen = new(false);

    private static readonly AsyncLocal<bool> FrozenV1Executing = new();

    public bool IsActive => Enabled && !FrozenV1Executing.Value;

    /// <summary>Marks the current asynchronous flow as frozen-V1 execution until disposed.</summary>
    public static IDisposable EnterFrozenV1()
    {
        var previous = FrozenV1Executing.Value;
        FrozenV1Executing.Value = true;
        return new Restore(previous);
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => FrozenV1Executing.Value = previous;
    }
}
