using System.Text.Json.Serialization;
using Shokofin.Events.Interfaces;

namespace Shokofin.SignalR.Models;

public class ReleaseSavedEventArgs : IReleaseSavedEventArgs {
    /// <inheritdoc />
    [JsonInclude, JsonPropertyName("FileID")]
    public int FileId { get; set; }
}
