using MQTTnet.Server;
using Meshtastic.Protobufs;
using Google.Protobuf;
using Serilog;
using MQTTnet.Protocol;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Formatting.Compact;
using Meshtastic.Crypto;
using Meshtastic;
using Meshtastic.Mqtt;
using CommandLine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

Configuration config = null!;

// Entrypoint
await RunMqttServer(args);
return;

async Task RunMqttServer(string[] args)
{
    string? logsFolder = null;
    
    _ = CommandLine.Parser.Default.ParseArguments<CommandLineArguments>(args)
        .MapResult(commandLineArguments =>
            {
                logsFolder = commandLineArguments.LogStoragePath;
                config = LoadConfigurationFromFile(commandLineArguments.ConfigFilePath);
                return 0;
            },
            errors => throw new Exception("Could not load settings."));
    
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console(new RenderedCompactJsonFormatter())
        .WriteTo.File(new RenderedCompactJsonFormatter(), Path.Combine(logsFolder!, "log.json"), rollingInterval: RollingInterval.Hour)
        .CreateLogger();
    
    using var mqttServer = new MqttServerFactory()
        .CreateMqttServer(BuildMqttServerOptions());
    ConfigureMqttServer(mqttServer);
    
    using var host = CreateHostBuilder(args).Build();
    await host.StartAsync();
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

    await mqttServer.StartAsync();
    
    await SetupGracefulShutdown(mqttServer, lifetime, host);
}

MqttServerOptions BuildMqttServerOptions()
{
    var options = new MqttServerOptionsBuilder()
        .WithDefaultEndpoint()  //  I wasted countless hours because this line was Without instead of With...
        .Build();

    return options;
}

void ConfigureMqttServer(MqttServer mqttServer)
{
    mqttServer.InterceptingPublishAsync += HandleInterceptingPublish;
    mqttServer.InterceptingSubscriptionAsync += HandleInterceptingSubscription;
    mqttServer.ValidatingConnectionAsync += HandleValidatingConnection;
}

async Task HandleInterceptingPublish(InterceptingPublishEventArgs args)
{
    try
    {
        if (args.ApplicationMessage.Payload.Length == 0)
        {
            Log.Logger.Warning("Received empty payload on topic {@Topic} from {@ClientId}",
                args.ApplicationMessage.Topic, args.ClientId);
            args.ProcessPublish = false;
            return;
        }

        var serviceEnvelope = ServiceEnvelope.Parser.ParseFrom(args.ApplicationMessage.Payload);

        if (!IsValidServiceEnvelope(serviceEnvelope))
        {
            Log.Logger.Warning(
                "Service envelope or packet is malformed. Blocking packet on topic {@Topic} from {@ClientId}",
                args.ApplicationMessage.Topic, args.ClientId);
            args.ProcessPublish = false;
            return;
        }

        // Spot for any async operations we might want to perform
        await Task.FromResult(0);

        var data = DecryptMeshPacket(serviceEnvelope);

        // Uncomment to block unrecognized packets
        if (data == null)
        {
            Log.Logger.Warning("Service envelope does not contain a valid packet. Blocking packet");
            args.ProcessPublish = false;
            return;
        }

        LogReceivedMessage(args.ApplicationMessage.Topic, args.ClientId, data);
        args.ProcessPublish = true;
    }
    catch (InvalidProtocolBufferException)
    {
        Log.Logger.Warning("Failed to decode presumed protobuf packet. Blocking");
        args.ProcessPublish = false;
    }
    catch (Exception ex)
    {
        Log.Logger.Error("Exception occurred while processing packet on {@Topic} from {@ClientId}: {@Exception}",
            args.ApplicationMessage.Topic, args.ClientId, ex.Message);
        args.ProcessPublish = false;
    }
}

Task HandleInterceptingSubscription(InterceptingSubscriptionEventArgs args)
{
    args.ProcessSubscription = true;

    var clientId = args.ClientId;

    // Check if it does NOT match an allowed topic partial path
    if (config.Broker.AllowedTopicPaths.Count > 0 &&
        !config.Broker.AllowedTopicPaths.Any(s => args.TopicFilter.Topic.StartsWith(s)))
    {
        Log.Logger.Warning("Failed subscription attempt by {@ClientId} on blocked topic {@Topic}",
            clientId, args.TopicFilter.Topic);
        args.Response.ReasonCode = MqttSubscribeReasonCode.NotAuthorized;
        args.ProcessSubscription = false;
    }

    // Check if it matches a blocked topic
    if (config.Broker.BlockedTopicPaths.Count > 0 &&
        config.Broker.BlockedTopicPaths.Any(s => args.TopicFilter.Topic.StartsWith(s)))
    {
        Log.Logger.Warning("Failed subscription attempt by {@ClientId} on blocked topic {@Topic}",
            clientId, args.TopicFilter.Topic);
        args.Response.ReasonCode = MqttSubscribeReasonCode.NotAuthorized;
        args.ProcessSubscription = false;
    }

    if (args.ProcessSubscription)
    {
        Log.Logger.Information("Successful subscription attempt by {@ClientId} on topic {@Topic}",
            clientId, args.TopicFilter.Topic);
    }

    return Task.CompletedTask;
}

Task HandleValidatingConnection(ValidatingConnectionEventArgs args)
{
    var clientId = args.ClientId;
    
    if (args.UserName != config.Broker.Username || args.Password != config.Broker.Password)
    {
        Log.Logger.Warning("Failed login attempt by {@ClientId} (username: {@Username})",
            clientId, args.UserName);
        args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
    }
    else
    {
        Log.Logger.Information("Successful login attempt by {@ClientId}",
            clientId);
        args.ReasonCode = MqttConnectReasonCode.Success;
    }

    return Task.CompletedTask;
}

bool IsValidServiceEnvelope(ServiceEnvelope serviceEnvelope)
{
    return !(string.IsNullOrWhiteSpace(serviceEnvelope.ChannelId) ||
             string.IsNullOrWhiteSpace(serviceEnvelope.GatewayId) ||
             serviceEnvelope.Packet == null ||
             serviceEnvelope.Packet.Id < 1 ||
             serviceEnvelope.Packet.From < 1 ||
             serviceEnvelope.Packet.Encrypted == null ||
             serviceEnvelope.Packet.Encrypted.Length < 1 ||
             serviceEnvelope.Packet.Decoded != null);
}

void LogReceivedMessage(string topic, string clientId, Data? data)
{
    if (data?.Portnum == PortNum.TextMessageApp)
    {
        Log.Logger.Information("Received text message on topic {@Topic} from {@ClientId}: {@Message}",
            topic, clientId, data.Payload.ToStringUtf8());
    }
    else
    {
        Log.Logger.Information("Received packet on topic {@Topic} from {@ClientId} with port number: {@PortNumber}",
            topic, clientId, data?.Portnum);
    }
}

static Data? DecryptMeshPacket(ServiceEnvelope serviceEnvelope)
{
    var nonce = new NonceGenerator(serviceEnvelope.Packet.From, serviceEnvelope.Packet.Id).Create();
    var decrypted =
        PacketEncryption.TransformPacket(serviceEnvelope.Packet.Encrypted.ToByteArray(), nonce, Resources.DEFAULT_PSK);
    var payload = Data.Parser.ParseFrom(decrypted);

    if (payload.Portnum > PortNum.UnknownApp && payload.Payload.Length > 0)
        return payload;

    return null;
}

async Task SetupGracefulShutdown(MqttServer mqttServer, IHostApplicationLifetime lifetime, IHost host)
{
    var ended = new ManualResetEventSlim();
    var starting = new ManualResetEventSlim();

    AssemblyLoadContext.Default.Unloading += ctx =>
    {
        starting.Set();
        Log.Logger.Debug("Waiting for completion");
        ended.Wait();
    };

    starting.Wait();

    Log.Logger.Debug("Received signal gracefully shutting down");
    await mqttServer.StopAsync();
    Thread.Sleep(500);
    ended.Set();
    
    lifetime.StopApplication();
    await host.WaitForShutdownAsync();
}

static IHostBuilder CreateHostBuilder(string[] args)
{
    return Host.CreateDefaultBuilder(args)
        .UseConsoleLifetime()
        .ConfigureServices((hostContext, services) => { services.AddSingleton(Console.Out); });
}

static Configuration LoadConfigurationFromFile(string filePath)
{
    if (!File.Exists(filePath))
    {
        throw new Exception("Config file path not found.");
    }

    var contents = File.ReadAllText(filePath);

    var deserializer = new DeserializerBuilder()
        .WithNamingConvention(PascalCaseNamingConvention.Instance)
        .Build();

    return deserializer.Deserialize<Configuration>(contents);
}