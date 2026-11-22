using TeleClean.Core.Models;
using TeleClean.Tests.Mocks;
using TL;
using Xunit;

namespace TeleClean.Tests;

public class LeaveDispatcherTests
{
    public enum DispatchAction
    {
        Channels_LeaveChannel,
        Messages_DeleteChatUser,
        Messages_DeleteHistory,
        Unknown
    }

    /// <summary>
    /// Pure dispatcher function matching TelegramService.LeaveDialogAsync routing contract.
    /// </summary>
    public static DispatchAction GetDispatchAction(DialogType type)
    {
        return type switch
        {
            DialogType.Channel or DialogType.Supergroup => DispatchAction.Channels_LeaveChannel,
            DialogType.Group => DispatchAction.Messages_DeleteChatUser,
            DialogType.Bot or DialogType.User or DialogType.Deleted => DispatchAction.Messages_DeleteHistory,
            _ => DispatchAction.Unknown
        };
    }

    /// <summary>
    /// Pure RPC error mapper matching TelegramService.LeaveDialogAsync catch-block contract.
    /// </summary>
    public static LeaveResult MapRpcException(RpcException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (ex.Code == 420) // FLOOD_WAIT_X
        {
            return LeaveResult.RateLimited;
        }
        if (ex.Code == 400 && ex.Message.Contains("CHANNEL_PRIVATE", StringComparison.OrdinalIgnoreCase))
        {
            return LeaveResult.Forbidden;
        }
        if (ex.Code == 400 && ex.Message.Contains("CHANNEL_INVALID", StringComparison.OrdinalIgnoreCase))
        {
            return LeaveResult.NotFound;
        }
        if (ex.Code == 403)
        {
            return LeaveResult.Forbidden;
        }

        return LeaveResult.Error;
    }

    [Theory]
    [InlineData(DialogType.Channel)]
    [InlineData(DialogType.Supergroup)]
    public void Dispatcher_ChannelsAndSupergroups_RoutesToChannelsLeaveChannel(DialogType type)
    {
        var action = GetDispatchAction(type);
        Assert.Equal(DispatchAction.Channels_LeaveChannel, action);
    }

    [Fact]
    public void Dispatcher_BasicGroups_RoutesToMessagesDeleteChatUser()
    {
        var action = GetDispatchAction(DialogType.Group);
        Assert.Equal(DispatchAction.Messages_DeleteChatUser, action);
    }

    [Theory]
    [InlineData(DialogType.Bot)]
    [InlineData(DialogType.User)]
    [InlineData(DialogType.Deleted)]
    public void Dispatcher_BotsUsersAndDeleted_RoutesToMessagesDeleteHistory(DialogType type)
    {
        var action = GetDispatchAction(type);
        Assert.Equal(DispatchAction.Messages_DeleteHistory, action);
    }

    [Fact]
    public async Task FakeTelegramService_LeaveDialog_TracksInvocationsAndUpdatesState()
    {
        var fake = new FakeTelegramService();
        var channel = new DialogItem { Id = -1001, Title = "Channel", DialogType = DialogType.Channel, IsJoined = true };
        var group = new DialogItem { Id = -2002, Title = "Group", DialogType = DialogType.Group, IsJoined = true };
        var bot = new DialogItem { Id = -3001, Title = "Bot", DialogType = DialogType.Bot, IsJoined = true };
        var deleted = new DialogItem { Id = -4001, Title = "Deleted", DialogType = DialogType.Deleted, IsJoined = false };

        var res1 = await fake.LeaveDialogAsync(channel);
        var res2 = await fake.LeaveDialogAsync(group);
        var res3 = await fake.LeaveDialogAsync(bot);
        var res4 = await fake.LeaveDialogAsync(deleted);

        Assert.Equal(LeaveResult.Success, res1);
        Assert.Equal(LeaveResult.Success, res2);
        Assert.Equal(LeaveResult.Success, res3);
        Assert.Equal(LeaveResult.Success, res4);

        Assert.False(channel.IsJoined);
        Assert.False(group.IsJoined);
        Assert.False(bot.IsJoined);

        Assert.Equal(4, fake.LeaveCalls.Count);
        Assert.Equal((-1001, DialogType.Channel), fake.LeaveCalls[0]);
        Assert.Equal((-2002, DialogType.Group), fake.LeaveCalls[1]);
        Assert.Equal((-3001, DialogType.Bot), fake.LeaveCalls[2]);
        Assert.Equal((-4001, DialogType.Deleted), fake.LeaveCalls[3]);
    }

    [Theory]
    [InlineData(420, "FLOOD_WAIT_30", LeaveResult.RateLimited)]
    [InlineData(420, "FLOOD_WAIT_300", LeaveResult.RateLimited)]
    [InlineData(400, "CHANNEL_PRIVATE", LeaveResult.Forbidden)]
    [InlineData(400, "CHANNEL_PRIVATE_ERROR", LeaveResult.Forbidden)]
    [InlineData(400, "CHANNEL_INVALID", LeaveResult.NotFound)]
    [InlineData(403, "CHAT_ADMIN_REQUIRED", LeaveResult.Forbidden)]
    [InlineData(500, "INTERNAL_SERVER_ERROR", LeaveResult.Error)]
    [InlineData(400, "OTHER_BAD_REQUEST", LeaveResult.Error)]
    public void RpcErrorMapping_MapsCodesToExpectedLeaveResults(int code, string message, LeaveResult expected)
    {
        var rpcEx = new RpcException(code, message, 0);
        var result = MapRpcException(rpcEx);
        Assert.Equal(expected, result);
    }
}
