using System.ComponentModel.DataAnnotations;

namespace BeaconRelay.LpdReceiver.Data;

public sealed class VirtualPrinterRecord
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string VirtualPrinterName { get; set; } = string.Empty;

    public int ListenPort { get; set; }

    [Required]
    [MaxLength(256)]
    public string QueueName { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? Description { get; set; }

    public ProtocolType Protocol { get; set; }
}
