namespace SeeFit.Fits;

public sealed class FitsKeyword
{
    public string Keyword { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Comment { get; init; } = string.Empty;
}
