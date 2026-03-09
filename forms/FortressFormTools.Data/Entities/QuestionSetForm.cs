using System.ComponentModel.DataAnnotations.Schema;

namespace FortressFormTools.Data.Entities;

/// <summary>
/// Many-to-many join: which forms are included in a question set.
/// </summary>
public class QuestionSetForm
{
    public int QuestionSetId { get; set; }
    public int FormLibraryId { get; set; }

    [ForeignKey(nameof(QuestionSetId))]
    public QuestionSet? QuestionSet { get; set; }

    [ForeignKey(nameof(FormLibraryId))]
    public FormLibrary? FormLibrary { get; set; }
}
