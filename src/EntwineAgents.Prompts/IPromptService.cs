
namespace EntwineAgents.Prompts;

public interface IPromptService
{
    Task<string> GetPromptAsync(string promptKey, int? clientId = null);
    Task<string?> GetModelOverrideAsync(string promptKey, int? clientId = null);
    Task<List<PromptTemplate>> GetAllPromptsAsync(int? clientId = null);
    Task<PromptTemplate> UpsertPromptAsync(PromptTemplate template);
    Task ResetToDefaultAsync(string promptKey, int clientId);
    Task<List<PromptTemplate>> GetHistoryAsync(string promptKey, int? clientId = null);
    Task<PromptTemplate?> RollbackAsync(string promptKey, int? clientId, int version);
}
