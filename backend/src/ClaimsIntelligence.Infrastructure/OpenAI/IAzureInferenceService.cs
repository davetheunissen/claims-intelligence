using Azure.AI.Inference;

namespace ClaimsIntelligence.Infrastructure.OpenAI;

public interface IAzureInferenceService
{
    Task<ChatCompletions> CompleteAsync(
        string model,
        IReadOnlyList<ChatRequestMessage> messages,
        ChatCompletionsOptions? options = null,
        CancellationToken cancellationToken = default);
}
