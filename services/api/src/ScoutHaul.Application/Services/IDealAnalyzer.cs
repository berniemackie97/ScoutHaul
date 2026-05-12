using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Application.Services;

public interface IDealAnalyzer
{
    DealAnalysisResult Analyze(DealAnalysisInput input);
}
