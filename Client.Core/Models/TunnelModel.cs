using System;

namespace Client.Core.Models;

/// <summary>
/// Represents a configured tunnel available to the user.
/// </summary>
public class TunnelModel
{
    /// <summary>
    /// Unique identifier for the tunnel.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly name of the tunnel.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the tunnel (e.g., active, inactive).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// The public URL where the tunnel can be accessed.
    /// </summary>
    public string PublicUrl { get; set; } = string.Empty;

    /// <summary>
    /// The local target address and port (e.g., localhost:8080).
    /// </summary>
    public string LocalTarget { get; set; } = string.Empty;

    /// <summary>
    /// The public port if applicable.
    /// </summary>
    public int PublicPort { get; set; }

    /// <summary>
    /// The creation date of the tunnel.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
