using ClaimsIntelligence.Domain.Pipeline;

namespace ClaimsIntelligence.Domain.Interfaces;

public interface IPipelineStep
{
    string StepName { get; }
    Task<DataPipeline> ExecuteAsync(DataPipeline pipeline, CancellationToken cancellationToken);
}
