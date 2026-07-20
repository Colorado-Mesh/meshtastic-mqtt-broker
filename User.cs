namespace Meshtastic.Mqtt;

public class UserCacheException : Exception
{
    internal UserCacheException(string message) : base(message)
    {
    }
}

public class User(string username, string password, List<string> allowedTopicPaths, List<string>? blockedTopicPaths)
{
    private string Username { get; set; } = username;
    private string Password { get; set; } = password;
    private List<string> AllowedTopicPaths { get; set; } = allowedTopicPaths;
    private List<string>? BlockedTopicPaths { get; set; } = blockedTopicPaths;

    internal string GetHash() => GetHash(Username);

    internal bool AllowedToSubscribe(string topic)
    {
        // Shortcut, user is allowed to sub to any $SYS
        if (topic == "$SYS" && AllowedTopicPaths.Contains("$SYS"))
        {
            return true;
        }
        
        // Shortcut, user is allowed to sub to anything
        if (AllowedTopicPaths.Contains("#"))
        {
            return true;
        }
        
        // Check if it does NOT match an allowed topic partial path
        if (AllowedTopicPaths.Count > 0 &&
            !AllowedTopicPaths.Any(topic.StartsWith))
        {
            return false;
        }
        

        // Check if it matches a blocked topic
        if (BlockedTopicPaths?.Count > 0 &&
            BlockedTopicPaths.Any(topic.StartsWith))
        {
            return false;
        }

        return true;
    }

    static internal string GetHash(string username) =>
        $"{username}".GetHashCode().ToString();
}