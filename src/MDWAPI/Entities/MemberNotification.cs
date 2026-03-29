using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("MemberNotifications", Schema = "mbw")]
public class MemberNotification
{
    [Key] public long NotificationId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(50)] public string NotificationType { get; set; } = default!;
    [Required, MaxLength(200)] public string Title { get; set; } = default!;
    [Required, MaxLength(1000)] public string Message { get; set; } = default!;
    [MaxLength(50)] public string? RefType { get; set; }
    [MaxLength(100)] public string? RefId { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? DisplayUntil { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;
}
