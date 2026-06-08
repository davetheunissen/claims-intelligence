namespace ClaimsIntelligence.Domain.Pipeline;

public class DataPipeline
{
    public string ProcessId { get; set; } = string.Empty;
    public PipelineStatus PipelineStatus { get; set; } = new();
    public List<FileDetails> Files { get; set; } = [];

    public FileDetails AddFile(string fileName, ArtifactType artifactType)
    {
        var file = new FileDetails
        {
            Id = Guid.NewGuid().ToString(),
            ProcessId = PipelineStatus.ProcessId ?? ProcessId,
            Name = fileName,
            MimeType = MimeTypeDetection.TryGetMimeType(fileName),
            ArtifactType = artifactType,
            ProcessedBy = PipelineStatus.ActiveStep
        };
        Files.Add(file);
        return file;
    }

    public StepResult? GetStepResult(string stepName)
        => PipelineStatus.GetStepResult(stepName);

    public StepResult? GetPreviousStepResult()
        => PipelineStatus.GetPreviousStepResult();

    public List<FileDetails> GetSourceFiles()
        => Files.Where(f => f.ArtifactType == ArtifactType.SourceContent).ToList();
}
