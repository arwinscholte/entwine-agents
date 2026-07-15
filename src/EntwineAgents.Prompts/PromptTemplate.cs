namespace EntwineAgents.Prompts;

public class PromptTemplate
{
    public int PromptTemplateId { get; set; }
    public string PromptKey { get; set; } = string.Empty;
    public int? ClientId { get; set; }
    public string PromptText { get; set; } = string.Empty;
    public string? ModelId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public string? ChangeSummary { get; set; }
    public bool IncludeInQualityAnalysis { get; set; } = false;
    public bool IncludeInOptimizer { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // No Client navigation: this type lives in the open Prompts package; the private app's DbContext
    // configures the ClientId foreign key shadow-style (HasOne<Client>() without a navigation property).
}
