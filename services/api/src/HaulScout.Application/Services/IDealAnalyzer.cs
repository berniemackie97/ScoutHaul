using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

public interface IDealAnalyzer
{
    DealAnalysisResult Analyze(DealAnalysisInput input);
}
