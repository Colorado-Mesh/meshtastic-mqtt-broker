using CommandLine;

namespace Meshtastic.Mqtt;

public class CommandLineArguments
{
    [Value(0)]
    public required string ConfigFilePath { get; set; }
    
    [Value(1)]
    public required string LogStoragePath { get; set; }
}