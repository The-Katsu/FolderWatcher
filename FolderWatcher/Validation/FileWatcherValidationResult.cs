namespace FolderWatcher.Validation;

public record FileWatcherValidationResult(
    bool IsPathValid, 
    bool IsCronValid, 
    List<Exception> Exceptions);