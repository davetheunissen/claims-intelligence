using Azure.AI.Inference;

namespace ClaimsIntelligence.Infrastructure.OpenAI;

public class AzureInferenceService(ChatCompletionsClient client) : IAzureInferenceService
{
    public async Task<ChatCompletions> CompleteAsync(
        string model,
        IReadOnlyList<ChatRequestMessage> messages,
        ChatCompletionsOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? new ChatCompletionsOptions();
        effectiveOptions.Model = model;

        foreach (var message in messages)
            effectiveOptions.Messages.Add(message);

        var response = await client.CompleteAsync(effectiveOptions, cancellationToken);
        return response.Value;
    }
}
