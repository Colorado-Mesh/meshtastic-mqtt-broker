// ReSharper disable CollectionNeverUpdated.Global

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
namespace Meshtastic.Mqtt;

// ReSharper disable once ClassNeverInstantiated.Global
public class Configuration
{
    public MqttBrokerConfiguration Broker { get; set; }
}

// ReSharper disable once ClassNeverInstantiated.Global
public class MqttBrokerConfiguration
{
    public string Username { get; set; }
    public string Password { get; set; }
    public List<string> AllowedTopicPaths { get; set; }
    public List<string> BlockedTopicPaths { get; set; }
}