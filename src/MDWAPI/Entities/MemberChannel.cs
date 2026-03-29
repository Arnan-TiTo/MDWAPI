using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("MemberChannels", Schema = "mbw")]
public class MemberChannel
{
    [Key] public int ChannelId { get; set; }

    [Required, MaxLength(50)] public string ChannelCode { get; set; } = default!;
    [Required, MaxLength(200)] public string ChannelName { get; set; } = default!;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
