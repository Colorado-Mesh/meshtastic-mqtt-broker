// ReSharper disable CollectionNeverUpdated.Global
// ReSharper disable ClassNeverInstantiated.Global

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
namespace Meshtastic.Mqtt;

public class Configuration
{
    public BrokerConfiguration Broker { get; set; }

    public User GetUser(string username, string password)
    {
        foreach (var user in Broker.Users)
        {
            if (username == user.Username && password == user.Password)
            {
                return user.ToUser();
            }
        }

        throw new Exception("No matching user");
    }
}

public class BrokerConfiguration
{
    public UserConfiguration[] Users { get; set; }
}

public class UserConfiguration
{
    public string Username { get; set; }
    public string Password { get; set; }
    public List<string> AllowedTopicPaths { get; set; }
    public List<string>? BlockedTopicPaths { get; set; }

    internal User ToUser() => new(username: Username, password: Password, allowedTopicPaths: AllowedTopicPaths,
        blockedTopicPaths: BlockedTopicPaths);
}