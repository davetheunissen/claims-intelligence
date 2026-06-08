namespace ClaimsIntelligence.Infrastructure.ContentUnderstanding.Models;

public record CuSpan(int Offset, int Length);

public record CuWord(string Content, CuSpan Span, float Confidence, string Source, IReadOnlyList<float> Polygon);

public record CuLine(string Content, string Source, CuSpan Span, IReadOnlyList<float> Polygon);

public record CuParagraph(string Content, string Source, CuSpan Span, IReadOnlyList<float> Polygon);

public record CuPage(
    int PageNumber,
    float Angle,
    float Width,
    float Height,
    IReadOnlyList<CuSpan> Spans,
    IReadOnlyList<CuWord> Words,
    IReadOnlyList<CuLine> Lines,
    IReadOnlyList<CuParagraph> Paragraphs);

public record CuDocumentContent(
    string Markdown,
    string Kind,
    int StartPageNumber,
    int EndPageNumber,
    string Unit,
    IReadOnlyList<CuPage> Pages);

public record CuResultData(
    string AnalyzerId,
    string ApiVersion,
    string CreatedAt,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CuDocumentContent> Contents);

public record CuAnalyzedResult(string Id, string Status, CuResultData Result);
