using Microsoft.Extensions.Configuration;

namespace FolderWatcher;

public sealed class ConfigurationValidator
{
    private readonly IConfiguration _config;

    public ConfigurationValidator(IConfiguration config) => _config = config;

    public bool ValidatePath() => 
        Directory.Exists(_config.GetValue<string>("Path"));
}