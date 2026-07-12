using Hound.Grocery.Config;
using Hound.Grocery.Services;

namespace Hound.Grocery.Tests.Services;

[TestClass]
public class PersonaPromptTests
{
    [TestMethod]
    public void BuildParserSystemPrompt_IncludesPersonaName_AndTone()
    {
        var settings = new TelegramSettings { PersonaName = "Chef", PersonaTone = "warm and brisk" };

        var prompt = PersonaPrompt.BuildParserSystemPrompt(settings);

        StringAssert.Contains(prompt, "Chef");
        StringAssert.Contains(prompt, "warm and brisk");
    }

    [TestMethod]
    public void BuildParserSystemPrompt_ContainsInjectionGuard()
    {
        var prompt = PersonaPrompt.BuildParserSystemPrompt(new TelegramSettings());

        StringAssert.Contains(prompt, PersonaPrompt.InjectionGuardMarker);
    }

    [TestMethod]
    public void BuildParserSystemPrompt_FallsBackToDefaults_WhenSettingsBlank()
    {
        var settings = new TelegramSettings { PersonaName = "  ", PersonaTone = "" };

        var prompt = PersonaPrompt.BuildParserSystemPrompt(settings);

        StringAssert.Contains(prompt, "Sous");
    }

    [TestMethod]
    public void BuildParserSystemPrompt_RequestsJsonOnly()
    {
        var prompt = PersonaPrompt.BuildParserSystemPrompt(new TelegramSettings());

        StringAssert.Contains(prompt, "intents");
        StringAssert.Contains(prompt, "JSON");
    }
}
