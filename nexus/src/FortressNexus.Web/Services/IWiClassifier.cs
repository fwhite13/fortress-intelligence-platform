using FortressNexus.Web.Models.DTOs;

namespace FortressNexus.Web.Services;

public interface IWiClassifier
{
    WiTemplateType ClassifyStory(AdoWorkItemDto story);
    bool ShouldGenerateTestCases(AdoWorkItemDto story);
    bool IsExternalDependency(AdoWorkItemDto wi);
    string? ExtractExternalOwner(AdoWorkItemDto wi);
}

public enum WiTemplateType
{
    Standard,
    Infrastructure,
    Migration,
    TestCase
}
