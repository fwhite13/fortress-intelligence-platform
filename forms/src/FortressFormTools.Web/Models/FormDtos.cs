namespace FortressFormTools.Web.Models;

public class FormUploadResponse
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class FormListItem
{
    public int Id { get; set; }
    public string CarrierName { get; set; } = string.Empty;
    public string FormName { get; set; } = string.Empty;
    public string FormType { get; set; } = string.Empty;
    public int? PageCount { get; set; }
    public int FieldCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FormDetailDto
{
    public int Id { get; set; }
    public string CarrierName { get; set; } = string.Empty;
    public string FormName { get; set; } = string.Empty;
    public string FormType { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? VerticalHint { get; set; }
    public int? PageCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<FormFieldDto> Fields { get; set; } = new();
}

public class FormFieldDto
{
    public int Id { get; set; }
    public string FieldLabel { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string? SectionName { get; set; }
    public int? PageNumber { get; set; }
    public decimal? AiConfidence { get; set; }
    public int? DictionaryFieldId { get; set; }
    public string? ValidationRules { get; set; }
    public int? SortOrder { get; set; }
}

public class BulkUploadRequest
{
    public string CarrierName { get; set; } = string.Empty;
    public string FormType { get; set; } = "Carrier";
}

public class DictionaryFieldModel
{
    public int Id { get; set; }
    public string FieldCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? FieldType { get; set; }
    public string? Description { get; set; }
}
