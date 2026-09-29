using System.Runtime.Serialization;

namespace ABB.Analyze.VisualStudio.Models;

[DataContract]
internal sealed class MutationAnalysisResult
{
    [DataMember(Name = "description")]
    public string Description { get; set; } = string.Empty;
}
