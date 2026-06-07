using Coffer.Core.Ai;

namespace Coffer.Infrastructure.Chat;

/// <summary>
/// A single <b>read-only</b> data tool the chat model may call (Phase 7, doc 04). Exposes the
/// vendor-neutral descriptor (<see cref="ToAiTool"/>) the provider advertises and an executor that
/// takes the model's raw JSON arguments and returns a serialized JSON result. Tools never mutate
/// state and never throw on bad arguments — they return an <c>{"error": …}</c> payload so the model
/// can self-correct (doc 04 "Hallucinated tool args"). The orchestrator anonymises the result
/// before it returns to the model (hard rule #7).
/// </summary>
public interface IChatTool
{
    string Name { get; }

    string Description { get; }

    string ParametersJsonSchema { get; }

    Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct);

    AiTool ToAiTool() => new(Name, Description, ParametersJsonSchema);
}
