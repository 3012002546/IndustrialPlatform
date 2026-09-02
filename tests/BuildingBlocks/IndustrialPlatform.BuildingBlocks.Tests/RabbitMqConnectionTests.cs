using System.Reflection;
using IndustrialPlatform.EventBus.Connection;
using IndustrialPlatform.EventBus.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class RabbitMqConnectionTests
{
    [Fact]
    public async Task DisposeAsync_WhenCloseRaisesShutdown_ReleasesTheOriginalConnection()
    {
        var sut = new RabbitMqConnection(
            Options.Create(new RabbitMqOptions()),
            NullLogger<RabbitMqConnection>.Instance);
        var connection = DispatchProxy.Create<IConnection, ShutdownOnCloseConnectionProxy>();
        var proxy = (ShutdownOnCloseConnectionProxy)(object)connection;
        SetPrivateField(sut, "_connection", connection);
        RegisterShutdownHandler(sut, connection);

        await sut.DisposeAsync();

        Assert.True(proxy.DisposeCalled);
    }

    private static void RegisterShutdownHandler(RabbitMqConnection sut, IConnection connection)
    {
        var eventInfo = typeof(IConnection).GetEvent(nameof(IConnection.ConnectionShutdownAsync));
        var method = typeof(RabbitMqConnection).GetMethod(
            "OnConnectionShutdownAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(eventInfo);
        Assert.NotNull(method);
        var handler = Delegate.CreateDelegate(eventInfo!.EventHandlerType!, sut, method!);
        eventInfo.AddEventHandler(connection, handler);
    }

    private static void SetPrivateField(RabbitMqConnection sut, string fieldName, IConnection connection)
    {
        var field = typeof(RabbitMqConnection).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(sut, connection);
    }

    public class ShutdownOnCloseConnectionProxy : DispatchProxy
    {
        private Delegate? _shutdownHandler;

        public bool DisposeCalled { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod);
            if (targetMethod!.Name == "add_ConnectionShutdownAsync")
            {
                _shutdownHandler = (Delegate)args![0]!;
                return null;
            }

            if (targetMethod.Name == "CloseAsync")
            {
                var shutdown = new ShutdownEventArgs(
                    ShutdownInitiator.Application,
                    0,
                    "test close",
                    new object(),
                    CancellationToken.None);
                _shutdownHandler?.DynamicInvoke(this, shutdown);
                return Task.CompletedTask;
            }

            if (targetMethod.Name == "Dispose")
            {
                DisposeCalled = true;
                return null;
            }

            return targetMethod.ReturnType == typeof(Task)
                ? Task.CompletedTask
                : targetMethod.ReturnType == typeof(ValueTask)
                    ? ValueTask.CompletedTask
                    : targetMethod.ReturnType.IsValueType
                        ? Activator.CreateInstance(targetMethod.ReturnType)
                        : null;
        }
    }
}
