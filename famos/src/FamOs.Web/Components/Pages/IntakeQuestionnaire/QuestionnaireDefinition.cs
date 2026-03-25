namespace FamOs.Web.Components.Pages.IntakeQuestionnaire;

using System.Text.Json;
using System.Text.Json.Nodes;

public static class QuestionnaireDefinition
{
    private static JsonObject? _def;

    public static JsonObject Load()
    {
        if (_def is not null) return _def;
        var asm = typeof(QuestionnaireDefinition).Assembly;
        // Resource name: FamOs.Web.Components.Pages.IntakeQuestionnaire.TruckingQuestionnaire.json
        using var stream = asm.GetManifestResourceStream(
            "FamOs.Web.Components.Pages.IntakeQuestionnaire.TruckingQuestionnaire.json")
            ?? throw new InvalidOperationException("TruckingQuestionnaire.json embedded resource not found");
        var doc = JsonNode.Parse(stream)!;
        _def = doc.AsObject();
        return _def;
    }

    public static JsonArray GetPages() => Load()["pages"]!.AsArray();
}
