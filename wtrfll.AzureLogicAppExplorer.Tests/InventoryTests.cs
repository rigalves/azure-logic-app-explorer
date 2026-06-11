using wtrfll.AzureLogicAppExplorer.Model;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace wtrfll.AzureLogicAppExplorer.Tests;

public class InventoryTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void ServiceBusTopics_RoundTripsThroughSnapshotJson()
    {
        var inventory = new Inventory
        {
            LogicApps = [],
            ScannedAt = DateTimeOffset.UtcNow,
            ServiceBusTopics =
            [
                new ServiceBusTopicInfo
                {
                    Namespace = "sb-namespace",
                    TopicName = "orders-topic",
                    Subscriptions = ["billing-sub", "shipping-sub"],
                },
            ],
        };

        var json = JsonSerializer.Serialize(inventory, SerializerOptions);
        var roundTripped = JsonSerializer.Deserialize<Inventory>(json, SerializerOptions);

        Assert.NotNull(roundTripped);
        var topic = Assert.Single(roundTripped.ServiceBusTopics);
        Assert.Equal("sb-namespace", topic.Namespace);
        Assert.Equal("orders-topic", topic.TopicName);
        Assert.Equal(["billing-sub", "shipping-sub"], topic.Subscriptions);
    }

    [Fact]
    public void Empty_HasNoServiceBusTopics()
    {
        Assert.Empty(Inventory.Empty.ServiceBusTopics);
    }
}
