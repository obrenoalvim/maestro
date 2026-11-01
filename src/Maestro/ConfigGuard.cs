using System.Text.Json;

namespace Maestro;

public sealed class ConfigGuard(AppPaths paths)
{
    private static readonly JsonDocumentOptions ConfigurationRules = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private MaestroOptions lastGood = MaestroOptions.Defaults;

    public string? Fault { get; private set; }

    public void Check(MaestroOptions bound)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(paths.Config), ConfigurationRules);
            Fault = null;
            lastGood = bound.Copy();
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            Fault = error.Message;
            lastGood.Copy().CopyTo(bound);
        }
    }
}
