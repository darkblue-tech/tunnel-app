using System;

namespace Client.Desktop.Models;

public class TunnelModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;
    public string LocalTarget { get; set; } = string.Empty;
    public int PublicPort { get; set; }
    public DateTime CreatedAt { get; set; }
}
