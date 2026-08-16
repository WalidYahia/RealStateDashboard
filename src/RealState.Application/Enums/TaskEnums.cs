namespace RealState.Application.Enums;

/// <summary>Lifecycle of a work task. Created as Todo; every status change appends a time-log entry.</summary>
public enum WorkTaskStatus
{
    Todo = 0,
    InProgress = 1,
    Completed = 2,
}

/// <summary>Task priority — two levels only.</summary>
public enum TaskSeverity
{
    Normal = 0,
    Urgent = 1,
}
