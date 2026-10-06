using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrewUpload.Schedule
{
    // The Survey Schedule's on-disk shapes (v66; the hand-off says these are frozen). Only the fields
    // the integration needs are named; everything else is carried along untouched in Extra so nothing
    // is lost when an override replaces a record.

    internal sealed class MasterFile
    {
        [JsonProperty("employees")] public List<EmployeeRecord> Employees { get; set; } = new List<EmployeeRecord>();
        [JsonProperty("pms")] public List<PmRecord> Pms { get; set; }
        [JsonProperty("activities")] public ActivityLists Activities { get; set; }
    }

    internal sealed class EmployeeRecord
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("active")] public bool? Active { get; set; }
    }

    internal sealed class PmRecord
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
    }

    internal sealed class ActivityLists
    {
        [JsonProperty("field")] public List<string> Field { get; set; } = new List<string>();
        [JsonProperty("office")] public List<string> Office { get; set; } = new List<string>();
    }

    internal sealed class PmFeed
    {
        [JsonProperty("projects")] public List<ProjectRecord> Projects { get; set; } = new List<ProjectRecord>();
        [JsonProperty("assignments")] public List<AssignmentRecord> Assignments { get; set; } = new List<AssignmentRecord>();
    }

    internal sealed class ProjectRecord
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("pmId")] public string PmId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("jobNum")] public string JobNum { get; set; }
        [JsonProperty("taskNum")] public string TaskNum { get; set; }
        [JsonProperty("updatedAt")] public string UpdatedAt { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    internal sealed class AssignmentRecord
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("employeeId")] public string EmployeeId { get; set; }
        [JsonProperty("projectId")] public string ProjectId { get; set; }
        [JsonProperty("label")] public string Label { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("start")] public string Start { get; set; }
        [JsonProperty("end")] public string End { get; set; }
        [JsonProperty("comments")] public string Comments { get; set; }
        [JsonProperty("task")] public string Task { get; set; }
        [JsonProperty("withIds")] public List<string> WithIds { get; set; } = new List<string>();
        [JsonProperty("pmId")] public string PmId { get; set; }
        [JsonProperty("updatedAt")] public string UpdatedAt { get; set; }
        [JsonProperty("approval")] public string Approval { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    /// <summary>One entry of pso-overrides.json: another PM's change to an entry or project they do not own.</summary>
    internal sealed class OverrideRecord
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("at")] public string At { get; set; }
        [JsonProperty("byPm")] public string ByPm { get; set; }
        [JsonProperty("op")] public string Op { get; set; }
        [JsonProperty("data")] public JToken Data { get; set; }
    }

    /// <summary>The schedule as the app assembles it.</summary>
    internal sealed class AssembledSchedule
    {
        public MasterFile Master { get; set; }
        public List<ProjectRecord> Projects { get; set; } = new List<ProjectRecord>();
        public List<AssignmentRecord> Assignments { get; set; } = new List<AssignmentRecord>();
    }
}
