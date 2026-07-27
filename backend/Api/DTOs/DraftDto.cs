namespace Api.DTOs;

public class SaveDraftRequest
{
    public List<string>? Days { get; set; }
}

public class DraftResponse
{
    public List<string> Days { get; set; } = [];
    public DateTime UpdatedAt { get; set; }
}
