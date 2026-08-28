using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ABB.Analyze.VisualStudio.Models
{

    [DataContract]
    internal sealed class CopilotModelList
    {
        [DataMember(Name = "currentModel")]
        public string CurrentModel { get; set; } =
            string.Empty;

        [DataMember(Name = "defaultModel")]
        public string DefaultModel { get; set; } =
            string.Empty;

        [DataMember(Name = "models")]
        public List<CopilotModelInfo> Models { get; set; } =
            new();
    }

    [DataContract]
    internal sealed class CopilotModelInfo
    {
        [DataMember(Name = "id")]
        public string Id { get; set; } =
            string.Empty;

        [DataMember(Name = "displayName")]
        public string DisplayName { get; set; } =
            string.Empty;

        [DataMember(Name = "provider")]
        public string Provider { get; set; } =
            string.Empty;

        [DataMember(Name = "available")]
        public bool Available { get; set; }

        public string DisplayText =>
            string.IsNullOrWhiteSpace(Provider)
                ? DisplayName
                : $"{DisplayName} ({Provider})";
    }
}