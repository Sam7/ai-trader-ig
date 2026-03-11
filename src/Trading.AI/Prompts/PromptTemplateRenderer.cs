namespace Trading.AI.Prompts;

public sealed class PromptTemplateRenderer
{
    public string Render(string template, IReadOnlyDictionary<string, string> variables)
    {
        var rendered = template;
        foreach (var (key, value) in variables)
        {
            rendered = rendered.Replace($"{{{{{key}}}}}", value, StringComparison.Ordinal);
        }

        return rendered;
    }
}
