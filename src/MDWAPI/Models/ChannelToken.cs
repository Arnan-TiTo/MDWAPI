using System;

namespace MDWAPI.Models
{
    public class ChannelToken
    {
        public int Id { get; set; }
        public string? Channel { get; set; }
        public string? Environment { get; set; }
        public string? AuthType { get; set; }
        public long? PartnerId { get; set; }
        public string? AppKey { get; set; }
        public long? AccountIdBig { get; set; }
        public string? AccountIdStr { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime AccessTokenExpAt { get; set; }
        public DateTime? RefreshTokenExpAt { get; set; }
        public string? Scope { get; set; }
        public string? Country { get; set; }
        public string? Region { get; set; }
        public int? CompanysId { get; set; }
        public int? PartnersId { get; set; }
        public string? TokenPayloadJson { get; set; }
        public string? ExtraJson { get; set; }
        public string? Note { get; set; }
        public bool isActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
