using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeleClean.Core.Models;

/// <summary>
/// The 6 dialog categories supported by TeleClean.
/// </summary>
[JsonConverter(typeof(DialogTypeJsonConverter))]
public enum DialogType
{
    Channel,
    Supergroup,
    Group,
    Bot,
    User,
    Deleted
}

/// <summary>
/// Custom converter supporting snake_case / lowercase strings for DialogType enum.
/// </summary>
public sealed class DialogTypeJsonConverter : JsonConverter<DialogType>
{
    public override DialogType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.Equals(str, "channel", StringComparison.OrdinalIgnoreCase)) return DialogType.Channel;
            if (string.Equals(str, "supergroup", StringComparison.OrdinalIgnoreCase)) return DialogType.Supergroup;
            if (string.Equals(str, "group", StringComparison.OrdinalIgnoreCase)) return DialogType.Group;
            if (string.Equals(str, "bot", StringComparison.OrdinalIgnoreCase)) return DialogType.Bot;
            if (string.Equals(str, "user", StringComparison.OrdinalIgnoreCase)) return DialogType.User;
            if (string.Equals(str, "deleted", StringComparison.OrdinalIgnoreCase)) return DialogType.Deleted;

            if (Enum.TryParse<DialogType>(str, true, out var parsed))
            {
                return parsed;
            }
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            return (DialogType)reader.GetInt32();
        }

        return DialogType.Channel;
    }

    public override void Write(Utf8JsonWriter writer, DialogType value, JsonSerializerOptions options)
    {
        var text = value switch
        {
            DialogType.Channel => "channel",
            DialogType.Supergroup => "supergroup",
            DialogType.Group => "group",
            DialogType.Bot => "bot",
            DialogType.User => "user",
            DialogType.Deleted => "deleted",
            _ => "channel"
        };
        writer.WriteStringValue(text);
    }
}
