namespace IntegrationConnector.Core.Enums;

public enum ConnectorType
{
    Rest = 1,
    Soap = 2,
    Ftp = 3,
    Database = 4,
    Queue = 5,
    File = 6,
    Sftp = 7,
    Email = 8,
    GraphQl = 9,
    Grpc = 10,
    LiteDb = 11
}

public enum PipelineTriggerType
{
    Manual = 1,
    Cron = 2,
    Interval = 3,
    Webhook = 4
}

public enum PipelineRunStatus
{
    Pending = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Retrying = 5,
    Cancelled = 6,
    PartiallySucceeded = 7
}

public enum StepDirection
{
    Source = 1,
    Target = 2
}

public enum TransformFunction
{
    None = 0,
    ToUpper = 1,
    ToLower = 2,
    Trim = 3,
    Default = 4,
    DateFormat = 5,
    Concat = 6,
    Number = 7,
    Constant = 8,
    Split = 9,
    Join = 10,
    RegexReplace = 11,
    Lookup = 12,
    Math = 13,
    Conditional = 14
}

public enum PayloadFormat
{
    Json = 1,
    Csv = 2,
    Xml = 3
}

public enum PipelineVersionStatus
{
    Draft = 1,
    InReview = 2,
    Published = 3
}

public enum UserRole
{
    Viewer = 1,
    Operator = 2,
    Admin = 3
}

public enum AggregationOperation
{
    Sum = 1,
    Count = 2,
    Avg = 3,
    Min = 4,
    Max = 5
}
